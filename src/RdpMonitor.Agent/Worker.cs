using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ServerApiClient _api;
    private readonly AgentIdentityStore _identityStore;
    private readonly LocalEventQueue _queue;
    private readonly RdpSessionMonitor _monitor;
    private readonly AuditMonitor _audit;
    private readonly AgentOptions _options;

    private AgentIdentity _identity = null!;

    public Worker(
        ILogger<Worker> logger,
        ServerApiClient api,
        AgentIdentityStore identityStore,
        LocalEventQueue queue,
        RdpSessionMonitor monitor,
        AuditMonitor audit,
        IOptions<AgentOptions> options)
    {
        _audit = audit;
        _logger = logger;
        _api = api;
        _identityStore = identityStore;
        _queue = queue;
        _monitor = monitor;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _identity = _identityStore.LoadOrCreate();
        if (!string.IsNullOrEmpty(_identity.ApiKey))
            _api.SetApiKey(_identity.ApiKey);

        await EnsureRegisteredAsync(stoppingToken);

        void Capture(RdpEventDto evt)
        {
            evt.AgentId = _identity.AgentId;
            _queue.Enqueue(evt);
            _logger.LogInformation("Event captured: {Type} user={User} ip={Ip} session={Session} {Details}",
                evt.EventType, evt.Username, evt.SourceIp, evt.SessionId, evt.Details);
        }

        _monitor.OnEvent += Capture;
        _monitor.Start();

        _audit.OnEvent += Capture;
        _audit.Start();

        var lastHeartbeat = DateTimeOffset.MinValue;
        var lastFlush = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;

            if (string.IsNullOrEmpty(_identity.ApiKey))
            {
                await EnsureRegisteredAsync(stoppingToken);
            }
            else
            {
                if ((now - lastFlush).TotalSeconds >= _options.QueueFlushIntervalSeconds)
                {
                    await FlushQueueAsync(stoppingToken);
                    lastFlush = now;
                }

                if ((now - lastHeartbeat).TotalSeconds >= _options.HeartbeatIntervalSeconds)
                {
                    await _api.SendHeartbeatAsync(new HeartbeatRequest { AgentId = _identity.AgentId, Timestamp = now }, stoppingToken);
                    lastHeartbeat = now;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsureRegisteredAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested && string.IsNullOrEmpty(_identity.ApiKey))
        {
            try
            {
                var request = new AgentRegistrationRequest
                {
                    AgentId = _identity.AgentId,
                    Hostname = Environment.MachineName,
                    IpAddress = GetLocalIpAddress(),
                    OsVersion = Environment.OSVersion.VersionString,
                    AgentVersion = typeof(Worker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                };

                var response = await _api.RegisterAsync(request, ct);
                if (response is not null && !string.IsNullOrEmpty(response.ApiKey))
                {
                    _identity.ApiKey = response.ApiKey;
                    _identityStore.Save(_identity);
                    _api.SetApiKey(_identity.ApiKey);
                    _logger.LogInformation("Successfully registered with server as agent {AgentId}", _identity.AgentId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Registration with server failed, retrying in {Seconds}s", backoff.TotalSeconds);
            }

            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 300));
        }
    }

    private async Task FlushQueueAsync(CancellationToken ct)
    {
        const int batchSize = 100;
        var pending = _queue.Peek(batchSize);
        if (pending.Count == 0) return;

        var ok = await _api.SendEventsAsync(new RdpEventBatchRequest { Events = pending }, ct);
        if (ok)
        {
            _queue.RemoveFirst(pending.Count);
            _logger.LogInformation("Flushed {Count} queued event(s) to server", pending.Count);
        }
        else
        {
            _logger.LogWarning("Server unreachable, {Count} event(s) remain queued locally", _queue.Count());
        }
    }

    private static string? GetLocalIpAddress()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return candidate?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }
}
