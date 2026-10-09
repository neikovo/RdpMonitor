using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Data;

namespace RdpMonitor.Server.Services;

public class EmailService
{
    private readonly SmtpOptions _smtp;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<SmtpOptions> smtp, ILogger<EmailService> logger)
    {
        _smtp = smtp.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_smtp.Host) && !string.IsNullOrWhiteSpace(_smtp.FromAddress);

    public async Task<bool> SendAlertAsync(IEnumerable<string> recipients, EventEntity evt, AgentEntity agent, CancellationToken ct)
    {
        var toList = recipients.ToList();
        if (!IsConfigured || toList.Count == 0) return false;

        try
        {
            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = _smtp.UseSsl,
            };
            if (!string.IsNullOrEmpty(_smtp.Username))
                client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);

            using var message = new MailMessage
            {
                From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
                Subject = $"[RDP Monitor] {evt.Severity} - {evt.EventType} on {agent.Hostname}",
                Body = BuildBody(evt, agent),
                IsBodyHtml = false,
            };
            foreach (var to in toList) message.To.Add(to);

            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send alert email for event {EventId}", evt.Id);
            return false;
        }
    }

    private static string BuildBody(EventEntity evt, AgentEntity agent)
    {
        return $"""
            An event with priority {evt.Severity} was detected.

            Server (agent):  {agent.Hostname} ({agent.IpAddress})
            Event type:      {evt.EventType}
            User:            {evt.Username ?? "-"}
            Source IP:       {evt.SourceIp ?? "-"}
            Session:         {evt.SessionId?.ToString() ?? "-"}
            Time:            {evt.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}
            Details:
            {evt.Details ?? "-"}

            --
            RDP Monitor Server
            """;
    }
}
