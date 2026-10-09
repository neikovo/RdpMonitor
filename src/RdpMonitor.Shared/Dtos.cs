namespace RdpMonitor.Shared;

/// <summary>Sent by the agent once at startup (and whenever identity info changes) to register itself with the server.</summary>
public class AgentRegistrationRequest
{
    public Guid AgentId { get; set; }
    public string Hostname { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? OsVersion { get; set; }
    public string AgentVersion { get; set; } = "";
}

public class AgentRegistrationResponse
{
    public Guid AgentId { get; set; }
    public string ApiKey { get; set; } = "";
}

/// <summary>Periodic "I'm alive" ping from the agent.</summary>
public class HeartbeatRequest
{
    public Guid AgentId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>A single RDP (or future process/file) event reported by the agent.</summary>
public class RdpEventDto
{
    public Guid AgentId { get; set; }
    public RdpEventType EventType { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public int? SessionId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Details { get; set; }
}

/// <summary>Batch submission of one or more events, so the agent can flush a local retry queue efficiently.</summary>
public class RdpEventBatchRequest
{
    public List<RdpEventDto> Events { get; set; } = new();
}
