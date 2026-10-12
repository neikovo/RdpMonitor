using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;
using RdpMonitor.Server.Services;
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
    public List<IgnoredSource> IgnoredSources { get; set; } = new();

    [BindProperty]
    public string NewRecipientEmail { get; set; } = "";
    [BindProperty]
    public string? NewRecipientName { get; set; }

    [BindProperty]
    public string NewIgnoreAddress { get; set; } = "";
    [BindProperty]
    public string? NewIgnoreNote { get; set; }
    [BindProperty]
    public bool NewIgnoreFailedLogons { get; set; }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        TypeSettings = await _db.EventTypeSettings.OrderBy(s => s.EventType).ToListAsync();
        Recipients = await _db.Recipients.OrderBy(r => r.Email).ToListAsync();
        IgnoredSources = await _db.IgnoredSources.OrderBy(s => s.Address).ToListAsync();
    }

    public async Task<IActionResult> OnPostAddIgnoredAsync()
    {
        var rule = IpFilter.ParseRule(NewIgnoreAddress, out var error);
        if (rule is null)
        {
            Error = error;
            return RedirectToPage();
        }

        var normalized = rule.ToString();
        if (await _db.IgnoredSources.AnyAsync(s => s.Address == normalized))
        {
            Error = $"{normalized} is already in the list.";
            return RedirectToPage();
        }

        _db.IgnoredSources.Add(new IgnoredSource
        {
            Address = normalized,
            Note = string.IsNullOrWhiteSpace(NewIgnoreNote) ? null : NewIgnoreNote.Trim(),
            IgnoreFailedLogons = NewIgnoreFailedLogons,
            Enabled = true,
        });
        await _db.SaveChangesAsync();
        Message = $"Events from {normalized} will no longer be recorded.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleIgnoredAsync(int id)
    {
        var s = await _db.IgnoredSources.FindAsync(id);
        if (s is not null)
        {
            s.Enabled = !s.Enabled;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteIgnoredAsync(int id)
    {
        var s = await _db.IgnoredSources.FindAsync(id);
        if (s is not null)
        {
            _db.IgnoredSources.Remove(s);
            await _db.SaveChangesAsync();
        }
        return RedirectToPage();
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
