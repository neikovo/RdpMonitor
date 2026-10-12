using Microsoft.EntityFrameworkCore;
using RdpMonitor.Server.Data;

namespace RdpMonitor.Server.Services;

/// <summary>
/// Deletes events older than the retention period the admin set in the dashboard (Settings -> Event retention).
/// The age is counted from the time the server RECEIVED the event, so events that an agent delivered late
/// after an outage are not removed the moment they arrive. 0 days = keep everything.
/// </summary>
public class RetentionService : BackgroundService
{
    public const string SettingKey = "EventRetentionDays";
    public const int DefaultDays = 7;
    public const int MaxDays = 3650;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(IServiceScopeFactory scopes, ILogger<RetentionService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await PurgeAsync(db, _logger, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatic event cleanup failed; it will be tried again in an hour");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The configured number of days, 0 if events are kept forever (also when the value is missing or invalid).</summary>
    public static async Task<int> GetDaysAsync(AppDbContext db, CancellationToken ct)
    {
        var raw = await db.AppSettings.AsNoTracking().Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var days) && days > 0 ? Math.Min(days, MaxDays) : 0;
    }

    public static async Task SetDaysAsync(AppDbContext db, int days, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync(new object[] { SettingKey }, ct);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = SettingKey, Value = days.ToString() });
        else row.Value = days.ToString();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes what is past the retention period. Returns how many events were removed.</summary>
    public static async Task<int> PurgeAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var days = await GetDaysAsync(db, ct);
        if (days <= 0) return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var removed = await db.Events.Where(e => e.ReceivedAt < cutoff).ExecuteDeleteAsync(ct);

        if (removed > 0)
        {
            logger.LogInformation("Event cleanup: deleted {Count} event(s) received before {Cutoff:yyyy-MM-dd HH:mm} UTC (retention {Days} days)", removed, cutoff, days);
            // keep the write-ahead log from staying large after a big delete
            if (removed >= 1000) await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", ct);
        }
        return removed;
    }
}
