using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Services;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Endpoints;

public static class AgentEndpoints
{
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
            var providedToken = http.Headers["X-Enrollment-Token"].ToString();
            var expectedToken = enrollmentOptions.Value.EnrollmentToken;
            if (string.IsNullOrEmpty(expectedToken) || providedToken != expectedToken)
            {
                logger.LogWarning("Rejected agent registration from {Ip}: invalid enrollment token", http.HttpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            if (request.AgentId == Guid.Empty || string.IsNullOrWhiteSpace(request.Hostname))
                return Results.BadRequest("AgentId and Hostname are required.");

            var apiKey = ApiKeyHelper.GenerateKey();
            var agent = await db.Agents.FindAsync(new object[] { request.AgentId }, ct);

            if (agent is null)
            {
                agent = new AgentEntity { Id = request.AgentId, RegisteredAt = DateTimeOffset.UtcNow };
                db.Agents.Add(agent);
            }

            agent.Hostname = request.Hostname;
            agent.IpAddress = request.IpAddress;
            agent.OsVersion = request.OsVersion;
            agent.AgentVersion = request.AgentVersion;
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

            var agentId = batch.Events[0].AgentId;
            var agent = await AuthenticateAgentAsync(http, db, agentId, ct);
            if (agent is null) return Results.Unauthorized();

            foreach (var dto in batch.Events)
            {
                if (dto.AgentId != agentId) continue; // a batch must belong to a single authenticated agent

                var entity = new EventEntity
                {
                    AgentId = agent.Id,
                    EventType = dto.EventType,
                    Username = dto.Username,
                    SourceIp = dto.SourceIp,
                    SessionId = dto.SessionId,
                    Details = dto.Details,
                    Timestamp = dto.Timestamp,
                    ReceivedAt = DateTimeOffset.UtcNow,
                };
                db.Events.Add(entity);
                await db.SaveChangesAsync(ct);

                await alertService.ProcessAsync(entity, agent, ct);
            }

            agent.LastSeenAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Stored {Count} event(s) from agent {Hostname}", batch.Events.Count, agent.Hostname);
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
