using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Data;

namespace RdpMonitor.Server.Services;

/// <summary>
/// Decides, for a newly stored event, whether it crosses the configured priority thresholds
/// and should be emailed — this is the "prioritize alerts" logic the admin controls from the UI
/// (per event-type severity/toggle in EventTypeSettings, per-agent mute) plus a global severity floor.
/// </summary>
public class AlertService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly AlertOptions _alertOptions;
    private readonly ILogger<AlertService> _logger;

    public AlertService(AppDbContext db, EmailService email, IOptions<AlertOptions> alertOptions, ILogger<AlertService> logger)
    {
        _db = db;
        _email = email;
        _alertOptions = alertOptions.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(EventEntity evt, AgentEntity agent, CancellationToken ct)
    {
        var typeSetting = await _db.EventTypeSettings.FindAsync(new object[] { evt.EventType }, ct);
        var severity = typeSetting?.Severity ?? RdpMonitor.Shared.AlertSeverity.Info;
        evt.Severity = severity;

        var emailEnabledForType = typeSetting?.EmailEnabled ?? false;
        var meetsGlobalFloor = severity >= _alertOptions.MinSeverityToEmail;

        if (agent.IsMuted || !emailEnabledForType || !meetsGlobalFloor)
        {
            evt.EmailSent = false;
            return;
        }

        var recipients = await _db.Recipients.Where(r => r.Enabled).Select(r => r.Email).ToListAsync(ct);
        if (recipients.Count == 0)
        {
            _logger.LogWarning("Event {EventId} qualifies for email but no enabled recipients are configured", evt.Id);
            evt.EmailSent = false;
            return;
        }

        var sent = await _email.SendAlertAsync(recipients, evt, agent, ct);
        evt.EmailSent = sent;
    }
}
