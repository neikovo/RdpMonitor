using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Pages;

public class SettingsModel : PageModel
{
    private readonly AppDbContext _db;

    public SettingsModel(AppDbContext db)
    {
        _db = db;
    }

    public List<EventTypeSetting> TypeSettings { get; set; } = new();
    public List<Recipient> Recipients { get; set; } = new();

    [BindProperty]
    public string NewRecipientEmail { get; set; } = "";
    [BindProperty]
    public string? NewRecipientName { get; set; }

    public async Task OnGetAsync()
    {
        TypeSettings = await _db.EventTypeSettings.OrderBy(s => s.EventType).ToListAsync();
        Recipients = await _db.Recipients.OrderBy(r => r.Email).ToListAsync();
    }

    public async Task<IActionResult> OnPostUpdateTypeAsync(RdpEventType eventType, AlertSeverity severity, bool emailEnabled)
    {
        var setting = await _db.EventTypeSettings.FindAsync(eventType);
        if (setting is not null)
        {
            setting.Severity = severity;
            setting.EmailEnabled = emailEnabled;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddRecipientAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewRecipientEmail))
        {
            _db.Recipients.Add(new Recipient { Email = NewRecipientEmail.Trim(), DisplayName = NewRecipientName, Enabled = true });
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleRecipientAsync(int id)
    {
        var r = await _db.Recipients.FindAsync(id);
        if (r is not null)
        {
            r.Enabled = !r.Enabled;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteRecipientAsync(int id)
    {
        var r = await _db.Recipients.FindAsync(id);
        if (r is not null)
        {
            _db.Recipients.Remove(r);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
