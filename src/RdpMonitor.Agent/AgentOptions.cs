namespace RdpMonitor.Agent;

public class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Base URL of the central server, e.g. https://rdpmonitor.mycompany.local</summary>
    public string ServerUrl { get; set; } = "https://localhost:5443";

    /// <summary>Shared enrollment token configured on the server, used only for the one-time registration call.</summary>
    public string EnrollmentToken { get; set; } = "";

    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public int QueueFlushIntervalSeconds { get; set; } = 15;

    /// <summary>Ignore TLS certificate validation errors (only for self-signed lab setups).</summary>
    public bool AllowInsecureTls { get; set; } = false;
}
