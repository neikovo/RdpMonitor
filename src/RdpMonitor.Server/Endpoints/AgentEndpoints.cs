using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Services;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Endpoints;

public static class AgentEndpoints
{
    private const int MaxEventsPerBatch = 500;

    public static void MapAgentEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/agents/register", async (
            AgentRegistrationRequest request,
            HttpRequest http,
            AppDbContext db,
            IOptions<AgentEnrollmentOptions> enrollmentOptions,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var expectedToken = enrollmentOptions.Value.EnrollmentToken;

            // The shipped placeholder is public on GitHub: while it is unchanged nobody may enroll agents.
            if (SecretPolicy.IsPlaceholder(expectedToken))
            {
                logger.LogError("Agent registration refused: Agent:EnrollmentToken in appsettings.json still has the default value. Set your own and restart the service.");
                return Results.Problem("Enrollment is disabled until the administrator sets Agent:EnrollmentToken.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var providedToken = http.Headers["X-Enrollment-Token"].ToString();
            if (!SecretCompare.Matches(providedToken, expectedToken))
            {
                logger.LogWarning("Rejected agent registration from {Ip}: invalid enrollment token", http.HttpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var hostname = InputSanitizer.Line(request.Hostname, 255);
            if (request.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(hostname))
                return Results.BadRequest("AgentId and Hostname are required.");

            var agent = await db.Agents.FindAsync(new object[] { request.AgentId }, ct);

            // An existing agent must NOT be re-enrolled with just the shared token: anyone who knows the token
            // (it is stored on every monitored server) could otherwise take over another agent's identity and
            // lock the real one out. Re-enrolling is allowed only with the agent's current key, or after the
            // admin deleted the agent in the dashboard.
            if (agent is not null)
            {
                var currentKey = http.Headers["X-Api-Key"].ToString();
                if (string.IsNullOrEmpty(currentKey) || !SecretCompare.Matches(ApiKeyHelper.Hash(currentKey), agent.ApiKeyHash))
                {
                    logger.LogWarning("Rejected re-registration of existing agent {AgentId} from {Ip}: delete the agent in the dashboard to enroll it again",
                        request.AgentId, http.HttpContext.Connection.RemoteIpAddress);
                    return Results.Conflict("This agent is already registered. Delete it in the dashboard (Agents) to enroll it again.");
                }
            }

            var apiKey = ApiKeyHelper.GenerateKey();
            if (agent is null)
            {
                agent = new AgentEntity { Id = request.AgentId, RegisteredAt = DateTimeOffset.UtcNow };
                db.Agents.Add(agent);
            }

            agent.Hostname = hostname!;
            agent.IpAddress = InputSanitizer.Line(request.IpAddress, 64);
            agent.OsVersion = InputSanitizer.Line(request.OsVersion, 200);
            agent.AgentVersion = InputSanitizer.Line(request.AgentVersion, 32);
            agent.ApiKeyHash = ApiKeyHelper.Hash(apiKey);
            agent.LastSeenAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);

            logger.LogInformation("Agent registered: {Hostname} ({AgentId})", agent.Hostname, agent.Id);

            return Results.Ok(new AgentRegistrationResponse { AgentId = agent.Id, ApiKey = apiKey });
        });

        api.MapPost("/agents/heartbeat", async (
            HeartbeatRequest request,
            HttpRequest http,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var agent = await AuthenticateAgentAsync(http, db, request.AgentId, ct);
            if (agent is null) return Results.Unauthorized();

            agent.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        api.MapPost("/events", async (
            RdpEventBatchRequest batch,
            HttpRequest http,
            AppDbContext db,
            AlertService alertService,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (batch.Events.Count == 0) return Results.Ok();
            if (batch.Events.Count > MaxEventsPerBatch)
                return Results.BadRequest($"At most {MaxEventsPerBatch} events per request.");

            var agentId = batch.Events[0].AgentId;
            var agent = await AuthenticateAgentAsync(http, db, agentId, ct);
            if (agent is null) return Results.Unauthorized();

            // Source addresses the admin chose to ignore (e.g. the jump server that connects over RDP).
            var ignoreRules = new List<(IpRule Rule, bool IgnoreFailed)>();
            foreach (var s in await db.IgnoredSources.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct))
            {
                var rule = IpFilter.ParseRule(s.Address, out _);
                if (rule is not null) ignoreRules.Add((rule, s.IgnoreFailedLogons));
            }
            var ignored = 0;

            foreach (var dto in batch.Events)
            {
                if (dto.AgentId != agentId) continue; // a batch must belong to a single authenticated agent

                if (ignoreRules.Count > 0 && !string.IsNullOrEmpty(dto.SourceIp)
                    && ignoreRules.Any(r => (dto.EventType != RdpEventType.RdpLogonFailed || r.IgnoreFailed) && IpFilter.Matches(r.Rule, dto.SourceIp)))
                {
                    ignored++;
                    continue;
                }

                if (!Enum.IsDefined(dto.EventType)) continue; // unknown event type (newer/hostile agent): never invent an alert for it

                var entity = new EventEntity
                {
                    AgentId = agent.Id,
                    EventType = dto.EventType,
                    Username = InputSanitizer.Line(dto.Username, 256),
                    SourceIp = InputSanitizer.Line(dto.SourceIp, 64),
                    SessionId = dto.SessionId,
                    Details = InputSanitizer.Text(dto.Details, 4000),
                    Timestamp = dto.Timestamp,
                    ReceivedAt = DateTimeOffset.UtcNow,
                };
                db.Events.Add(entity);
                await db.SaveChangesAsync(ct);

                await alertService.ProcessAsync(entity, agent, ct);
            }

            agent.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Stored {Count} event(s) from agent {Hostname} ({Ignored} ignored by source-address filter)",
                batch.Events.Count - ignored, agent.Hostname, ignored);
            return Results.Ok();
        });
    }

    private static async Task<AgentEntity?> AuthenticateAgentAsync(HttpRequest http, AppDbContext db, Guid agentId, CancellationToken ct)
    {
        var apiKey = http.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(apiKey) || agentId == Guid.Empty) return null;

        var hash = ApiKeyHelper.Hash(apiKey);
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId && a.ApiKeyHash == hash, ct);
        return agent;
    }
}
