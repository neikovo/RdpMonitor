using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

/// <summary>
/// File and process audit. Reads Security events 4663 (file access, gives the user) and 4688 (process created),
/// and uses FileSystemWatcher only to tell "created" from "modified" (4663 cannot).
/// Windows only writes these events if auditing is enabled — see setup-audit.ps1.
/// </summary>
public class AuditMonitor : IDisposable
{
    private readonly ILogger<AuditMonitor> _logger;
    private readonly AuditOptions _options;
    private readonly List<EventLogWatcher> _watchers = new();
    private readonly List<FileSystemWatcher> _fsWatchers = new();

    private readonly List<string> _folders = new();
    private readonly List<Regex> _ignore = new();
    private List<Regex> _cmdPatterns = new();

    private readonly ConcurrentDictionary<string, DateTime> _recentlyCreated = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _recentlySeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _rateLock = new();
    private DateTime _rateWindowStart = DateTime.UtcNow;
    private int _rateCount;
    private int _rateDropped;

    public event Action<RdpEventDto>? OnEvent;

    public AuditMonitor(ILogger<AuditMonitor> logger, IOptions<AuditOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public void Start()
    {
        foreach (var f in _options.FileFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            try { _folders.Add(AuditParser.NormalizeFolder(f)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Invalid audit folder '{Folder}' ignored", f); }
        }

        foreach (var p in _options.IgnoreFilePatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
            _ignore.Add(AuditParser.WildcardToRegex(p));

        _cmdPatterns = AuditParser.CompilePatterns(_options.SuspiciousCommandLinePatterns,
            (p, ex) => _logger.LogWarning("Bad command-line pattern '{Pattern}' ignored: {Error}", p, ex.Message));

        if (_folders.Count > 0)
        {
            foreach (var folder in _folders) StartFolderWatcher(folder);
            Subscribe("*[System[(EventID=4663)]]", HandleFileAccess, "file audit (4663)");
            _logger.LogInformation("File audit watching {Count} folder(s): {Folders}", _folders.Count, string.Join(", ", _folders));
        }
        else
        {
            _logger.LogInformation("File audit is off (Audit:FileFolders is empty)");
        }

        if (_options.MonitorProcesses)
        {
            Subscribe("*[System[(EventID=4688)]]", HandleProcessCreated, "process audit (4688)");
            _logger.LogInformation("Process audit on: {Names} name(s), {Patterns} command-line pattern(s)",
                _options.SuspiciousProcesses.Count, _cmdPatterns.Count);
        }
        else
        {
            _logger.LogInformation("Process audit is off (Audit:MonitorProcesses is false)");
        }
    }

    private void StartFolderWatcher(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Audit folder does not exist: {Folder}", folder);
                return;
            }
            var w = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName, InternalBufferSize = 64 * 1024 };
            w.Created += (_, e) =>
            {
                _recentlyCreated[e.FullPath] = DateTime.UtcNow;
                if (_recentlyCreated.Count > 5000) Prune(_recentlyCreated, TimeSpan.FromMinutes(2));
            };
            w.EnableRaisingEvents = true;
            _fsWatchers.Add(w);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not watch folder {Folder} for file creation; created files will be reported as modified", folder);
        }
    }

    private void Subscribe(string xpath, Action<EventRecord> handler, string what)
    {
        try
        {
            var watcher = new EventLogWatcher(new EventLogQuery("Security", PathType.LogName, xpath));
            watcher.EventRecordWritten += (_, e) =>
            {
                if (e.EventRecord is null) return;
                try { handler(e.EventRecord); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to process {What} event", what); }
                finally { e.EventRecord.Dispose(); }
            };
            watcher.Enabled = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not subscribe to the Security log for {What}", what);
        }
    }

    private void HandleProcessCreated(EventRecord record)
    {
        var data = RdpSessionMonitor.ExtractEventData(record);
        var evt = AuditParser.FromProcessCreation(data, record.TimeCreated, _options, _cmdPatterns);
        if (evt is not null) Emit(evt, dedupKey: null);
    }

    private void HandleFileAccess(EventRecord record)
    {
        var data = RdpSessionMonitor.ExtractEventData(record);
        var evt = AuditParser.FromFileAccess(
            data, record.TimeCreated, _folders, _ignore,
            isDirectory: Directory.Exists,
            wasJustCreated: p => _recentlyCreated.TryGetValue(p, out var t) && DateTime.UtcNow - t < TimeSpan.FromSeconds(30));
        if (evt is null) return;

        // One save of a document produces several 4663 events: report the first one per user+file within the window.
        var path = evt.Details?.Split(';')[0] ?? "";
        Emit(evt, dedupKey: $"{evt.Username}|{path}");
    }

    private void Emit(RdpEventDto evt, string? dedupKey)
    {
        var now = DateTime.UtcNow;

        if (dedupKey is not null)
        {
            var window = TimeSpan.FromSeconds(Math.Max(0, _options.DedupSeconds));
            if (_recentlySeen.TryGetValue(dedupKey, out var last) && now - last < window) return;
            _recentlySeen[dedupKey] = now;
            if (_recentlySeen.Count > 5000) Prune(_recentlySeen, TimeSpan.FromMinutes(2));
        }

        lock (_rateLock)
        {
            if (now - _rateWindowStart >= TimeSpan.FromMinutes(1))
            {
                if (_rateDropped > 0)
                    _logger.LogWarning("Audit flood protection dropped {Count} event(s) in the last minute (limit {Limit}/min)", _rateDropped, _options.MaxEventsPerMinute);
                _rateWindowStart = now;
                _rateCount = 0;
                _rateDropped = 0;
            }
            if (_rateCount >= _options.MaxEventsPerMinute) { _rateDropped++; return; }
            _rateCount++;
        }

        OnEvent?.Invoke(evt);
    }

    private static void Prune(ConcurrentDictionary<string, DateTime> map, TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        foreach (var kv in map)
            if (kv.Value < cutoff) map.TryRemove(kv.Key, out _);
    }

    public void Dispose()
    {
        foreach (var w in _watchers) { w.Enabled = false; w.Dispose(); }
        foreach (var w in _fsWatchers) w.Dispose();
        _watchers.Clear();
        _fsWatchers.Clear();
    }
}
