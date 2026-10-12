using System.Security.Cryptography;
using System.Text;

namespace RdpMonitor.Server.Services;

/// <summary>Comparison of secrets in constant time, so response timing does not leak how many characters matched.</summary>
public static class SecretCompare
{
    public static bool Matches(string? a, string? b)
    {
        // hash first: FixedTimeEquals needs equal lengths, and hashing hides the length too
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a ?? ""));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b ?? ""));
        return CryptographicOperations.FixedTimeEquals(ha, hb) && !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b);
    }
}

/// <summary>Detects secrets that were never changed from the placeholder shipped in appsettings.json (which is public on GitHub).</summary>
public static class SecretPolicy
{
    public static bool IsPlaceholder(string? secret) =>
        string.IsNullOrWhiteSpace(secret) || secret.TrimStart().StartsWith("CHANGE-ME", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Cleans text that comes from monitored machines before it is stored, logged or put in an e-mail header.</summary>
public static class InputSanitizer
{
    /// <summary>Single line: control characters (incl. CR/LF) removed, length limited.</summary>
    public static string? Line(string? value, int maxLength)
    {
        if (value is null) return null;
        var sb = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value)
        {
            if (sb.Length >= maxLength) break;
            if (!char.IsControl(ch)) sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Multi-line text: only tab and line feed are kept as control characters, length limited.</summary>
    public static string? Text(string? value, int maxLength)
    {
        if (value is null) return null;
        var sb = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value)
        {
            if (sb.Length >= maxLength) break;
            if (ch == '\r') continue;
            if (!char.IsControl(ch) || ch == '\n' || ch == '\t') sb.Append(ch);
        }
        return sb.ToString();
    }
}
