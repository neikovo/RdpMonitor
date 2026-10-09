using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;
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
}
