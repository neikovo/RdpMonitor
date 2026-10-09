using RdpMonitor.Shared;

namespace RdpMonitor.Server.Data;

public class AgentEntity
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? OsVersion { get; set; }
    public string? AgentVersion { get; set; }

    /// <summary>SHA-256 hash of the API key. The plaintext key is only ever returned to the agent at registration time.</summary>
    public string ApiKeyHash { get; set; } = "";

    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When true, this agent's events are stored but never trigger email alerts (e.g. a known jump box).</summary>
    public bool IsMuted { get; set; }

    public List<EventEntity> Events { get; set; } = new();
}

public class EventEntity
{
    public int Id { get; set; }
    public Guid AgentId { get; set; }
    public AgentEntity? Agent { get; set; }

    public RdpEventType EventType { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public int? SessionId { get; set; }
    public string? Details { get; set; }

    public AlertSeverity Severity { get; set; }
    public bool EmailSent { get; set; }

    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Per event-type policy: how severe it is considered and whether it should ever trigger an email.</summary>
public class EventTypeSetting
{
    public RdpEventType EventType { get; set; }
    public AlertSeverity Severity { get; set; }
    public bool EmailEnabled { get; set; }
}

public class Recipient
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
}
