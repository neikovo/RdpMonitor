namespace RdpMonitor.Agent;

/// <summary>
/// Settings for the file and process audit. All lists are empty by default on purpose:
/// the real defaults live in appsettings.json (the binder would otherwise append to code defaults).
/// </summary>
public class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Folders to watch for file changes (recursive). Empty = file audit is off.</summary>
    public List<string> FileFolders { get; set; } = new();

    /// <summary>File name patterns (with * and ?) that are never reported, e.g. *.tmp or ~$*.</summary>
    public List<string> IgnoreFilePatterns { get; set; } = new();

    /// <summary>Report only starts of the processes listed below / matching the command-line patterns.</summary>
    public bool MonitorProcesses { get; set; }

    /// <summary>Program names (with or without .exe) that are always reported.</summary>
    public List<string> SuspiciousProcesses { get; set; } = new();

    /// <summary>Case-insensitive regular expressions matched against the full command line.</summary>
    public List<string> SuspiciousCommandLinePatterns { get; set; } = new();

    /// <summary>The same user touching the same file again within this many seconds is reported once.</summary>
    public int DedupSeconds { get; set; } = 10;

    /// <summary>Safety valve against floods: more audit events per minute than this are dropped.</summary>
    public int MaxEventsPerMinute { get; set; } = 120;
}
