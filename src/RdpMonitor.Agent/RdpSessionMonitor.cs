using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

/// <summary>
/// Watches the two Windows event logs that carry RDP session activity and turns matching
/// records into <see cref="RdpEventDto"/> instances via <see cref="OnEvent"/>.
///
/// - Microsoft-Windows-TerminalServices-LocalSessionManager/Operational: session logon/logoff/
///   disconnect/reconnect (event IDs 21, 23, 24, 25) — reliable session lifecycle signal.
/// - Security log: 4624 (successful logon, LogonType 10 = RemoteInteractive) and 4625 (failed logon,
///   LogonType 10) — captures failed RDP attempts and gives us the source IP for successful ones.
/// </summary>
public class RdpSessionMonitor : IDisposable
{
    private readonly ILogger<RdpSessionMonitor> _logger;
    private readonly List<EventLogWatcher> _watchers = new();

    public event Action<RdpEventDto>? OnEvent;

    public RdpSessionMonitor(ILogger<RdpSessionMonitor> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        TrySubscribe(
            "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
            "*[System[(EventID=21 or EventID=23 or EventID=24 or EventID=25)]]",
            HandleTerminalServicesEvent);

        TrySubscribe(
            "Security",
            "*[System[(EventID=4625)]]",
            HandleSecurityEvent);
    }

    private void TrySubscribe(string logName, string xpathQuery, Action<EventRecord> handler)
    {
        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, xpathQuery);
            var watcher = new EventLogWatcher(query);
            watcher.EventRecordWritten += (_, e) =>
            {
                if (e.EventRecord is null) return;
                try
                {
                    handler(e.EventRecord);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process event record from {Log}", logName);
                }
                finally
                {
                    e.EventRecord.Dispose();
                }
            };
            watcher.Enabled = true;
            _watchers.Add(watcher);
            _logger.LogInformation("Subscribed to event log: {Log}", logName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not subscribe to event log {Log}. This event source will not be monitored.", logName);
        }
    }

    private void HandleTerminalServicesEvent(EventRecord record)
    {
        var data = ExtractEventData(record);
        var eventType = record.Id switch
        {
            21 => RdpEventType.RdpLogonSuccess,
            23 => RdpEventType.RdpLogoff,
            24 => RdpEventType.RdpDisconnect,
            25 => RdpEventType.RdpReconnect,
            _ => (RdpEventType?)null
        };
        if (eventType is null) return;

        data.TryGetValue("User", out var user);
        data.TryGetValue("Address", out var address);
        int? sessionId = data.TryGetValue("SessionID", out var sid) && int.TryParse(sid, out var s) ? s : null;

        Raise(eventType.Value, user, address, sessionId, record.TimeCreated);
    }

    private void HandleSecurityEvent(EventRecord record)
    {
        var data = ExtractEventData(record);

        // 4625 = failed logon. With NLA enabled (default on Server 2022) a failed RDP attempt is logged
        // as LogonType 3 (network); without NLA it is LogonType 10 (RemoteInteractive).
        data.TryGetValue("LogonType", out var logonType);
        if (logonType != "3" && logonType != "10") return;

        data.TryGetValue("TargetUserName", out var user);
        data.TryGetValue("IpAddress", out var ip);
        data.TryGetValue("Status", out var status);
        data.TryGetValue("SubStatus", out var subStatus);

        // Skip empty names and machine-account noise
        if (string.IsNullOrEmpty(user) || user == "-" || user.EndsWith("$")) return;

        var details = $"LogonType={logonType}; Status={status}; SubStatus={subStatus}";
        Raise(RdpEventType.RdpLogonFailed, user, string.IsNullOrEmpty(ip) || ip == "-" ? null : ip, null, record.TimeCreated, details);
    }

    private void Raise(RdpEventType type, string? user, string? sourceIp, int? sessionId, DateTime? timeCreated, string? details = null)
    {
        OnEvent?.Invoke(new RdpEventDto
        {
            EventType = type,
            Username = user,
            SourceIp = sourceIp,
            SessionId = sessionId,
            Details = details,
            Timestamp = timeCreated.HasValue ? new DateTimeOffset(timeCreated.Value.ToUniversalTime()) : DateTimeOffset.UtcNow,
        });
    }

    internal static Dictionary<string, string> ExtractEventData(EventRecord record)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var xml = XDocument.Parse(record.ToXml());
            XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
            foreach (var dataNode in xml.Descendants(ns + "Data"))
            {
                var name = dataNode.Attribute("Name")?.Value;
                if (name is not null)
                    result[name] = dataNode.Value;
            }
        }
        catch
        {
            // ignore malformed XML; caller will just see missing fields
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            w.Enabled = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
