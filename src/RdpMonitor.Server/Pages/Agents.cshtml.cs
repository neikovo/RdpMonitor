using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;

namespace RdpMonitor.Server.Pages;

public class AgentsModel : PageModel
{
    private readonly AppDbContext _db;

    public AgentsModel(AppDbContext db)
    {
        _db = db;
    }

    public List<AgentEntity> Agents { get; set; } = new();
    public Dictionary<Guid, int> EventCounts { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        Agents = await _db.Agents.AsNoTracking().OrderBy(a => a.Hostname).ToListAsync();
        EventCounts = await _db.Events.AsNoTracking()
            .GroupBy(e => e.AgentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    /// <summary>Removes the agent and all its events. The caller must type the exact host name (checked here, not only in the browser).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string? confirmName)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null)
        {
            Error = "Agent not found.";
            return RedirectToPage();
        }

        if (!string.Equals((confirmName ?? "").Trim(), agent.Hostname, StringComparison.OrdinalIgnoreCase))
        {
            Error = $"The name does not match - agent {agent.Hostname} was NOT deleted.";
            return RedirectToPage();
        }

        _db.Agents.Remove(agent); // events are removed by cascade
        await _db.SaveChangesAsync();
        Message = $"Agent {agent.Hostname} and all of its events were deleted. The agent itself is not uninstalled on that server.";
        return RedirectToPage();
    }
}
