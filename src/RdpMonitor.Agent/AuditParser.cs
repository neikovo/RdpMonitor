using System.Globalization;
using System.Text.RegularExpressions;
using RdpMonitor.Shared;

namespace RdpMonitor.Agent;

/// <summary>
/// Pure functions that turn the fields of Security events 4688 (process created) and 4663 (object access)
/// into <see cref="RdpEventDto"/>. No I/O of its own, so it can be tested without Windows event logs.
/// </summary>
public static class AuditParser
{
    private const int AccessDelete = 0x10000;
    private const int AccessWriteData = 0x2;   // also "AddFile" on a directory
    private const int AccessAppendData = 0x4;  // also "AddSubdirectory" on a directory

    public static List<Regex> CompilePatterns(IEnumerable<string> patterns, Action<string, Exception>? onBad = null)
    {
        var result = new List<Regex>();
        foreach (var p in patterns.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            try { result.Add(new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200))); }
            catch (Exception ex) { onBad?.Invoke(p, ex); }
        }
        return result;
    }

    public static Regex WildcardToRegex(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public static string NormalizeFolder(string folder) =>
        Path.GetFullPath(folder.Trim()).TrimEnd('\\') + "\\";

    /// <summary>Event 4688. Returns null unless the process is on the suspicious list or its command line matches a pattern.</summary>
    public static RdpEventDto? FromProcessCreation(
        IReadOnlyDictionary<string, string> d, DateTime? time, AuditOptions options, IReadOnlyList<Regex> commandLinePatterns)
    {
        d.TryGetValue("NewProcessName", out var image);
        if (string.IsNullOrWhiteSpace(image)) return null;
        d.TryGetValue("CommandLine", out var cmd);
        d.TryGetValue("ParentProcessName", out var parent);

        var exe = Path.GetFileNameWithoutExtension(image);
        string? reason = null;

        if (options.SuspiciousProcesses.Any(n => string.Equals(Path.GetFileNameWithoutExtension(n.Trim()), exe, StringComparison.OrdinalIgnoreCase)))
            reason = $"програма от списъка: {exe}";

        if (reason is null && !string.IsNullOrEmpty(cmd))
        {
            foreach (var rx in commandLinePatterns)
            {
                try
                {
                    if (rx.IsMatch(cmd)) { reason = $"команден ред съвпада с: {rx}"; break; }
                }
                catch (RegexMatchTimeoutException) { /* skip pathological input */ }
            }
        }

        if (reason is null) return null;

        d.TryGetValue("NewProcessId", out var pid);
        d.TryGetValue("ProcessId", out var parentPid);
        d.TryGetValue("TokenElevationType", out var elevation);
        d.TryGetValue("SubjectLogonId", out var logonId);

        var lines = new List<string>
        {
            $"Програма: {image}",
            $"Команден ред: {Trunc(cmd, 600)}",
            $"Защо е отбелязана: {reason}",
            $"Стартирана от: {parent} (PID {HexToDec(parentPid)})",
            $"PID на процеса: {HexToDec(pid)}",
            $"Права: {ElevationText(elevation)}",
        };
        if (!string.IsNullOrWhiteSpace(logonId)) lines.Add($"Сесия на потребителя (LogonId): {logonId}");

        return new RdpEventDto
        {
            EventType = RdpEventType.ProcessStarted,
            Username = BuildUser(d),
            Timestamp = ToUtc(time),
            Details = string.Join("\n", lines),
        };
    }

    private static string HexToDec(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "?";
        hex = hex.Trim();
        var span = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.AsSpan(2) : hex.AsSpan();
        return int.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : hex;
    }

    private static string ElevationText(string? type) => type?.Trim() switch
    {
        "%%1937" => "повишени (стартирана като администратор)",
        "%%1938" => "стандартни (ограничени)",
        "%%1936" => "пълни, без UAC ограничение (напр. системен акаунт)",
        _ => "неизвестни",
    };

    /// <summary>
    /// Event 4663. Returns null for reads, for folders, for paths outside the watched folders and for ignored file names.
    /// </summary>
    public static RdpEventDto? FromFileAccess(
        IReadOnlyDictionary<string, string> d, DateTime? time, IReadOnlyList<string> normalizedFolders,
        IReadOnlyList<Regex> ignore, Func<string, bool> isDirectory, Func<string, bool> wasJustCreated)
    {
        d.TryGetValue("ObjectType", out var objectType);
        if (!string.Equals(objectType, "File", StringComparison.OrdinalIgnoreCase)) return null;

        d.TryGetValue("ObjectName", out var path);
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!normalizedFolders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return null;

        var name = Path.GetFileName(path);
        if (ignore.Any(rx => rx.IsMatch(name))) return null;

        d.TryGetValue("AccessMask", out var maskText);
        if (!TryParseMask(maskText, out var mask)) return null;

        RdpEventType type;
        if ((mask & AccessDelete) != 0)
        {
            type = RdpEventType.FileDeleted;
        }
        else if ((mask & (AccessWriteData | AccessAppendData)) != 0)
        {
            if (isDirectory(path)) return null; // a change inside a folder is reported for the file itself
            type = wasJustCreated(path) ? RdpEventType.FileCreated : RdpEventType.FileModified;
        }
        else
        {
            return null; // read / attribute access
        }

        d.TryGetValue("ProcessName", out var process);
        return new RdpEventDto
        {
            EventType = type,
            Username = BuildUser(d),
            Timestamp = ToUtc(time),
            Details = $"{path}; Програма: {process}",
        };
    }

    private static bool TryParseMask(string? text, out int mask)
    {
        mask = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask);
    }

    private static string? BuildUser(IReadOnlyDictionary<string, string> d)
    {
        d.TryGetValue("SubjectUserName", out var user);
        d.TryGetValue("SubjectDomainName", out var domain);
        if (string.IsNullOrEmpty(user)) return null;
        return string.IsNullOrEmpty(domain) || domain == "-" ? user : $"{domain}\\{user}";
    }

    private static DateTimeOffset ToUtc(DateTime? t) =>
        t.HasValue ? new DateTimeOffset(t.Value.ToUniversalTime()) : DateTimeOffset.UtcNow;

    private static string Trunc(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
