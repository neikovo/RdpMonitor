using System.Net;
using System.Net.Sockets;

namespace RdpMonitor.Server.Services;

/// <summary>An IP address or a network (CIDR) used by the "ignored source addresses" filter.</summary>
public sealed class IpRule
{
    public IPAddress Network { get; }
    public int PrefixLength { get; }
    public bool IsSingleAddress { get; }

    internal IpRule(IPAddress network, int prefixLength, bool single)
    {
        Network = network;
        PrefixLength = prefixLength;
        IsSingleAddress = single;
    }

    public bool Contains(IPAddress address)
    {
        address = IpFilter.Unmap(address);
        if (address.AddressFamily != Network.AddressFamily) return false;

        var a = address.GetAddressBytes();
        var n = Network.GetAddressBytes();
        var fullBytes = PrefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
            if (a[i] != n[i]) return false;

        var remainder = PrefixLength % 8;
        if (remainder == 0) return true;
        var mask = (byte)(0xFF << (8 - remainder));
        return (a[fullBytes] & mask) == (n[fullBytes] & mask);
    }

    public override string ToString() =>
        IsSingleAddress ? Network.ToString() : $"{Network}/{PrefixLength}";
}

public static class IpFilter
{
    /// <summary>Turns an IPv4-mapped IPv6 address (::ffff:1.2.3.4) into plain IPv4 so both spellings match.</summary>
    internal static IPAddress Unmap(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>Parses "1.2.3.4", "10.0.0.0/24", "2001:db8::1" or "2001:db8::/32". Returns null and an error text if invalid.</summary>
    public static IpRule? ParseRule(string? text, out string? error)
    {
        error = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) { error = "Enter an IP address or a network such as 10.0.0.0/24."; return null; }

        var addressText = text;
        int? prefix = null;
        var slash = text.IndexOf('/');
        if (slash >= 0)
        {
            addressText = text[..slash].Trim();
            if (!int.TryParse(text[(slash + 1)..].Trim(), out var p)) { error = "The part after / must be a number."; return null; }
            prefix = p;
        }

        var address = ParseAddress(addressText);
        if (address is null) { error = $"'{addressText}' is not a valid IP address."; return null; }

        var max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix is < 0 || prefix > max) { error = $"The prefix length must be between 0 and {max}."; return null; }

        var single = prefix is null || prefix == max;
        return new IpRule(address, prefix ?? max, single);
    }

    /// <summary>Parses a single address. Rejects shorthand such as "5" (which .NET would read as 0.0.0.5).</summary>
    public static IPAddress? ParseAddress(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        if (!text.Contains(':') && text.Split('.').Length != 4) return null; // IPv4 must have four parts
        if (!IPAddress.TryParse(text, out var address)) return null;
        return Unmap(address);
    }

    public static bool Matches(IpRule rule, string? sourceIp)
    {
        var address = ParseAddress(sourceIp);
        return address is not null && rule.Contains(address);
    }
}
