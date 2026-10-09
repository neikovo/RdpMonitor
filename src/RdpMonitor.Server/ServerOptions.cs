namespace RdpMonitor.Server;

public class AgentEnrollmentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Shared secret agents must present on their one-time /api/agents/register call.</summary>
    public string EnrollmentToken { get; set; } = "";
}

public class AdminOptions
{
    public const string SectionName = "Admin";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
}

public class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "RDP Monitor";
}

public class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Events below this severity never trigger email, regardless of per-type settings.</summary>
    public RdpMonitor.Shared.AlertSeverity MinSeverityToEmail { get; set; } = RdpMonitor.Shared.AlertSeverity.Medium;
}
