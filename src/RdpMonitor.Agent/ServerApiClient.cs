using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

public class ServerApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ServerApiClient> _logger;
    private readonly AgentOptions _options;
    private string? _apiKey;

    public ServerApiClient(HttpClient http, IOptions<AgentOptions> options, ILogger<ServerApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.ServerUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
        _http.DefaultRequestHeaders.Remove("X-Api-Key");
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<AgentRegistrationResponse?> RegisterAsync(AgentRegistrationRequest request, CancellationToken ct)
    {
        _http.DefaultRequestHeaders.Remove("X-Enrollment-Token");
        if (!string.IsNullOrEmpty(_options.EnrollmentToken))
            _http.DefaultRequestHeaders.Add("X-Enrollment-Token", _options.EnrollmentToken);

        var resp = await _http.PostAsJsonAsync("api/agents/register", request, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentRegistrationResponse>(cancellationToken: ct);
    }

    public async Task<bool> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/agents/heartbeat", request, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Heartbeat failed");
            return false;
        }
    }

    public async Task<bool> SendEventsAsync(RdpEventBatchRequest batch, CancellationToken ct)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("api/events", batch, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sending events failed");
            return false;
        }
    }
}
