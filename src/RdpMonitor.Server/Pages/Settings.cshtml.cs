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
    private readonly ILogger<SettingsModel> _logger;

    public SettingsModel(AppDbContext db, ILogger<SettingsModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int RetentionDaysCurrent { get; set; }
    public int StoredEventCount { get; set; }
    public DateTimeOffset? OldestEvent { get; set; }

    [BindProperty]
    public int RetentionDays { get; set; }

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

        RetentionDaysCurrent = await RetentionService.GetDaysAsync(_db, HttpContext.RequestAborted);
        RetentionDays = RetentionDaysCurrent;
        StoredEventCount = await _db.Events.CountAsync();
        OldestEvent = StoredEventCount == 0
            ? null
            : await _db.Events.AsNoTracking().OrderBy(e => e.ReceivedAt).Select(e => (DateTimeOffset?)e.ReceivedAt).FirstOrDefaultAsync();
    }

    public async Task<IActionResult> OnPostSaveRetentionAsync()
    {
        // Read the raw text: a value that is not a whole number must be refused, never silently turned into 0 ("keep forever").
        var raw = Request.Form["RetentionDays"].ToString().Trim();
        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days)
            || days < 0 || days > RetentionService.MaxDays)
        {
            Error = $"Enter a whole number of days between 1 and {RetentionService.MaxDays}, or 0 to keep events forever. Nothing was changed.";
            return RedirectToPage();
        }

        await RetentionService.SetDaysAsync(_db, days, HttpContext.RequestAborted);
        _logger.LogWarning("Event retention was set to {Days} day(s) from the dashboard by {Ip}", days, HttpContext.Connection.RemoteIpAddress);
        Message = days == 0
            ? "Events are now kept forever."
            : $"Events older than {days} day(s) are deleted automatically (the cleanup runs every hour, or press \"Clean up now\").";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCleanNowAsync()
    {
        var days = await RetentionService.GetDaysAsync(_db, HttpContext.RequestAborted);
        if (days == 0)
        {
            Error = "Nothing was deleted: events are kept forever (retention is 0).";
            return RedirectToPage();
        }

        var removed = await RetentionService.PurgeAsync(_db, _logger, HttpContext.RequestAborted);
        Message = $"Deleted {removed} event(s) older than {days} day(s).";
        return RedirectToPage();
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
