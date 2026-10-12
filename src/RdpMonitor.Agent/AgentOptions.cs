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

    /// <summary>
    /// SHA-256 fingerprint of the server's TLS certificate (hex; colons and spaces are ignored). When set, the agent
    /// accepts ONLY that certificate - this is the safe way to talk to a self-signed server. The server writes the value
    /// to C:\ProgramData\RdpMonitor\server-cert-sha256.txt.
    /// </summary>
    public string ServerCertificateSha256 { get; set; } = "";

    /// <summary>
    /// Accept ANY server certificate (weak: allows a man-in-the-middle to read the API key and send fake events).
    /// Ignored when ServerCertificateSha256 is set. Prefer pinning the certificate.
    /// </summary>
    public bool AllowInsecureTls { get; set; } = false;
}
