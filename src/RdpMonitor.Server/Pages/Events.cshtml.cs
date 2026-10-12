using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Services;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Pages;

public class EventsModel : PageModel
{
    private readonly AppDbContext _db;

    public EventsModel(AppDbContext db)
    {
        _db = db;
    }

    public List<EventEntity> Events { get; set; } = new();
    public Dictionary<Guid, string> AgentNames { get; set; } = new();

    public Guid? AgentId { get; set; }
    public AlertSeverity? MinSeverity { get; set; }

    public async Task OnGetAsync(Guid? agentId, AlertSeverity? minSeverity)
    {
        AgentId = agentId;
        MinSeverity = minSeverity;

        var query = _db.Events.AsNoTracking().AsQueryable();
        if (agentId.HasValue) query = query.Where(e => e.AgentId == agentId.Value);
        if (minSeverity.HasValue) query = query.Where(e => e.Severity >= minSeverity.Value);

        Events = await query.OrderByDescending(e => e.ReceivedAt).Take(300).ToListAsync();

        AgentNames = await _db.Agents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Hostname);
    }

    /// <summary>Downloads the events that match the current filters as a CSV file (newest first, at most 200,000 rows).</summary>
    public async Task<IActionResult> OnGetExportAsync(Guid? agentId, AlertSeverity? minSeverity)
    {
        var query = _db.Events.AsNoTracking().AsQueryable();
        if (agentId.HasValue) query = query.Where(e => e.AgentId == agentId.Value);
        if (minSeverity.HasValue) query = query.Where(e => e.Severity >= minSeverity.Value);

        var rows = await query.OrderByDescending(e => e.ReceivedAt).Take(200_000).ToListAsync();
        var agentNames = await _db.Agents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Hostname);

        var sb = new StringBuilder();
        CsvExport.Row(sb, "Time (local)", "Time (UTC)", "Agent", "Type", "Priority", "User", "Source IP", "Session", "Details", "E-mail sent");
        foreach (var e in rows)
        {
            CsvExport.Row(sb,
                e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                agentNames.GetValueOrDefault(e.AgentId, e.AgentId.ToString()),
                e.EventType.ToString(),
                e.Severity.ToString(),
                e.Username,
                e.SourceIp,
                e.SessionId?.ToString(),
                e.Details,
                e.EmailSent ? "Yes" : "No");
        }

        // UTF-8 with a byte-order mark so Excel shows non-Latin characters correctly
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"rdp-monitor-events-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
