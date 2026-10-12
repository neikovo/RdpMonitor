using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Services;

namespace RdpMonitor.Server.Pages;

public class AgentRow
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? OsVersion { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsOnline { get; set; }
    public bool IsMuted { get; set; }
    public int EventCount24h { get; set; }
    public int HighSeverityCount24h { get; set; }
}

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminOptions _admin;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(AppDbContext db, IOptions<AdminOptions> admin, ILogger<IndexModel> logger)
    {
        _db = db;
        _admin = admin.Value;
        _logger = logger;
    }

    public List<AgentRow> Agents { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var onlineThreshold = TimeSpan.FromMinutes(3);
        var since = now.AddHours(-24);

        var agents = await _db.Agents.AsNoTracking().OrderBy(a => a.Hostname).ToListAsync();
        var recentEvents = await _db.Events.AsNoTracking()
            .Where(e => e.ReceivedAt >= since)
            .Select(e => new { e.AgentId, e.Severity })
            .ToListAsync();

        Agents = agents.Select(a => new AgentRow
        {
            Id = a.Id,
            Hostname = a.Hostname,
            IpAddress = a.IpAddress,
            OsVersion = a.OsVersion,
            LastSeenAt = a.LastSeenAt,
            IsOnline = (now - a.LastSeenAt) <= onlineThreshold,
            IsMuted = a.IsMuted,
            EventCount24h = recentEvents.Count(e => e.AgentId == a.Id),
            HighSeverityCount24h = recentEvents.Count(e => e.AgentId == a.Id && e.Severity >= RdpMonitor.Shared.AlertSeverity.High),
        }).ToList();
    }

    public async Task<IActionResult> OnPostToggleMuteAsync(Guid id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is not null)
        {
            agent.IsMuted = !agent.IsMuted;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    /// <summary>Deletes only the stored events of one agent (the agent itself stays registered). Requires the admin password.</summary>
    public async Task<IActionResult> OnPostClearEventsAsync(Guid id, string? password)
    {
        var ok = !SecretPolicy.IsPlaceholder(_admin.Password) && SecretCompare.Matches(password, _admin.Password);
        if (!ok)
        {
            _logger.LogWarning("Delete-events confirmation FAILED from {Ip}", HttpContext.Connection.RemoteIpAddress);
            Error = "Wrong password - the events were NOT deleted.";
            return RedirectToPage();
        }

        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (agent is null)
        {
            Error = "Agent not found.";
            return RedirectToPage();
        }

        var deleted = await _db.Events.Where(e => e.AgentId == id).ExecuteDeleteAsync();
        Message = $"Deleted {deleted} event(s) of {agent.Hostname}.";
        return RedirectToPage();
    }
}
