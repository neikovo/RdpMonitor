using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RdpMonitor.Shared;

namespace RdpMonitor.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<EventTypeSetting> EventTypeSettings => Set<EventTypeSetting>();
    public DbSet<Recipient> Recipients => Set<Recipient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite has no native DateTimeOffset ordering/comparison support (EF Core throws on
        // ORDER BY / >,>=,<,<= over such columns), so store these as UTC ticks instead.
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));

        modelBuilder.Entity<AgentEntity>(e =>
        {
            e.HasIndex(a => a.ApiKeyHash);
            e.HasMany(a => a.Events).WithOne(ev => ev.Agent).HasForeignKey(ev => ev.AgentId);
            e.Property(a => a.RegisteredAt).HasConversion(dateTimeOffsetConverter);
            e.Property(a => a.LastSeenAt).HasConversion(dateTimeOffsetConverter);
        });

        modelBuilder.Entity<EventEntity>(e =>
        {
            e.HasIndex(ev => ev.Timestamp);
            e.HasIndex(ev => ev.AgentId);
            e.Property(ev => ev.Timestamp).HasConversion(dateTimeOffsetConverter);
            e.Property(ev => ev.ReceivedAt).HasConversion(dateTimeOffsetConverter);
        });

        modelBuilder.Entity<EventTypeSetting>().HasKey(s => s.EventType);

        // Sensible defaults so the admin doesn't have to configure anything before alerts start working.
        modelBuilder.Entity<EventTypeSetting>().HasData(
            new EventTypeSetting { EventType = RdpEventType.RdpLogonSuccess, Severity = AlertSeverity.Info, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.RdpLogonFailed, Severity = AlertSeverity.High, EmailEnabled = true },
            new EventTypeSetting { EventType = RdpEventType.RdpLogoff, Severity = AlertSeverity.Info, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.RdpDisconnect, Severity = AlertSeverity.Low, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.RdpReconnect, Severity = AlertSeverity.Low, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.SessionLocked, Severity = AlertSeverity.Info, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.SessionUnlocked, Severity = AlertSeverity.Info, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.ProcessStarted, Severity = AlertSeverity.Medium, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.ProcessStopped, Severity = AlertSeverity.Info, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.FileCreated, Severity = AlertSeverity.Medium, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.FileModified, Severity = AlertSeverity.Medium, EmailEnabled = false },
            new EventTypeSetting { EventType = RdpEventType.FileDeleted, Severity = AlertSeverity.High, EmailEnabled = false }
        );
    }
}
