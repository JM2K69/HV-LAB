using System.Text.RegularExpressions;

namespace HVLab.Services;

/// <summary>
/// Validation of user input before it reaches a PowerShell script or an answer file.
/// Each method returns a localization key describing the problem, or null when the value is valid.
/// </summary>
public static partial class InputValidator
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    [GeneratedRegex("^[A-Za-z0-9-]{1,15}$")]
    private static partial Regex ComputerNameRegex();

    [GeneratedRegex("^([A-Za-z0-9]{5}-){4}[A-Za-z0-9]{5}$")]
    private static partial Regex ProductKeyRegex();

    /// <summary>
    /// VM names become folder and file names (<c>VmsFolder\Name\Virtual Hard Disks\Name.vhdx</c>):
    /// no path separators, no "..", no invalid file-name characters, no trailing dot/space.
    /// </summary>
    public static string? CheckVmName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))                 return "Val_VmNameEmpty";
        if (name.Length > 100)                               return "Val_VmNameTooLong";
        if (name != name.Trim() || name.EndsWith('.'))       return "Val_VmNameEdges";
        if (name.Contains("..") || name.IndexOfAny(InvalidNameChars) >= 0) return "Val_VmNameChars";
        return null;
    }

    /// <summary>NetBIOS-compatible computer name: 1–15 letters, digits or hyphens, not only digits.</summary>
    public static string? CheckComputerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ComputerNameRegex().IsMatch(name)
            || name.All(char.IsDigit) || name.StartsWith('-') || name.EndsWith('-'))
            return "Val_ComputerName";
        return null;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex LinuxUserRegex();

    /// <summary>Linux account created by cloud-init: lowercase POSIX name, not root.</summary>
    public static string? CheckLinuxUser(string? name)
        => name is null || !LinuxUserRegex().IsMatch(name) || name == "root" ? "Val_LinuxUser" : null;

    /// <summary>Empty (no key) or one OpenSSH public key line.</summary>
    public static string? CheckSshPublicKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        return key.Contains('\n') || key.Contains('\r')
            || !(key.StartsWith("ssh-") || key.StartsWith("ecdsa-") || key.StartsWith("sk-"))
            ? "Val_SshKey" : null;
    }

    /// <summary>Dotted IPv4 address (a.b.c.d).</summary>
    public static string? CheckIpv4(string? address)
        => ParseIpv4(address) is null ? "Val_Ipv4" : null;

    public static uint? ParseIpv4(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var parts = address.Trim().Split('.');
        if (parts.Length != 4) return null;
        uint value = 0;
        foreach (var p in parts)
        {
            if (p.Length is 0 or > 3 || !p.All(char.IsAsciiDigit) || !byte.TryParse(p, out var b)) return null;
            value = (value << 8) | b;
        }
        return value;
    }

    public static string FormatIpv4(uint value)
        => $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}";

    /// <summary>DNS servers separated by commas, semicolons or spaces; null when one is not an IPv4 address.</summary>
    public static IReadOnlyList<string>? ParseDnsServers(string? list)
    {
        var items = (list ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        return items.All(i => ParseIpv4(i) is not null) ? items : null;
    }

    /// <summary>
    /// Static IPv4 plan of <paramref name="count"/> clones numbered from <paramref name="first"/>: prefix 8–30,
    /// gateway in the subnet, every address in the subnet, not the network or broadcast address, not the gateway.
    /// </summary>
    public static string? CheckStaticIpPlan(string? first, int prefixLength, string? gateway, int count)
    {
        if (ParseIpv4(first) is not { } ip || ParseIpv4(gateway) is not { } gw) return "Val_Ipv4";
        if (prefixLength is < 8 or > 30) return "Val_Prefix";
        var mask      = uint.MaxValue << (32 - prefixLength);
        var network   = ip & mask;
        var broadcast = network | ~mask;
        if ((gw & mask) != network) return "Val_GatewaySubnet";
        var last = (ulong)ip + (ulong)Math.Max(count, 1) - 1;
        if (ip == network || last >= broadcast || (gw >= ip && gw <= last)) return "Val_IpRange";
        return null;
    }

    /// <summary>Empty (no key) or the 5×5 product key format.</summary>
    public static string? CheckProductKey(string? key)
        => string.IsNullOrWhiteSpace(key) || ProductKeyRegex().IsMatch(key.Trim()) ? null : "Val_ProductKey";

    /// <summary>Derives a valid computer name from a VM name (invalid characters → '-', max 15).</summary>
    public static string ToComputerName(string vmName)
    {
        var chars = vmName.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name  = new string(chars).Trim('-');
        if (name.Length > 15) name = name[..15].TrimEnd('-');
        if (name.Length == 0 || name.All(char.IsDigit)) name = "VM-" + name;
        return name.Length > 15 ? name[..15] : name;
    }

    /// <summary>Throws when <paramref name="name"/> is not a valid VM name (defense in depth for the services).</summary>
    public static void EnsureVmName(string name)
    {
        if (CheckVmName(name) is { } key)
            throw new ArgumentException(LocalizationService.Instance[key], nameof(name));
    }
}
