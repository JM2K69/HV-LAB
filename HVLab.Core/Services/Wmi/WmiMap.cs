using System.Text.RegularExpressions;
using HVLab.Models;

namespace HVLab.Services.Wmi;

/// <summary>
/// Pure conversions of Hyper-V WMI values (unit-tested): VM states, generations, IDs inside InstanceID
/// and object paths, MAC and IP formats, switch types, NAT ownership, checkpoint tree, thumbnail pixels.
/// </summary>
public static partial class WmiMap
{
    /// <summary>Msvm_ComputerSystem / Msvm_SummaryInformation EnabledState → the state names of Get-VM.</summary>
    public static string StateName(long enabledState) => enabledState switch
    {
        2               => "Running",
        3               => "Off",
        6 or 32769      => "Saved",
        9 or 32768      => "Paused",
        10 or 32770     => "Starting",
        4 or 32774      => "Stopping",
        32773           => "Saving",
        32776           => "Pausing",
        32777           => "Resuming",
        11              => "Reset",
        _               => "Other",
    };

    /// <summary>"Microsoft:Hyper-V:SubType:1" → 1, anything else → 2.</summary>
    public static int Generation(string? virtualSystemSubType)
        => virtualSystemSubType?.EndsWith(":1", StringComparison.Ordinal) == true ? 1 : 2;

    /// <summary>Uptime in milliseconds → "hh:mm:ss", with "d." in front past one day (as Get-VM shows it).</summary>
    public static string FormatUptime(long milliseconds)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(milliseconds, 0));
        return t.Days > 0 ? t.ToString(@"d\.hh\:mm\:ss") : t.ToString(@"hh\:mm\:ss");
    }

    /// <summary>Heartbeat operational status: 2 → "OK", lost or no contact → "Lost", unknown → "".</summary>
    public static string Heartbeat(long? status) => status switch
    {
        2                => "OK",
        6 or 12 or 13    => "Lost",
        _                => "",
    };

    [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex GuidRegex();

    /// <summary>
    /// GUID of the owning VM in a resource InstanceID ("Microsoft:&lt;VM&gt;\…", "Microsoft:GuestNetwork\&lt;VM&gt;\…"),
    /// upper case; null for definitions ("Microsoft:Definition\…") and IDs without a GUID.
    /// </summary>
    public static string? OwnerId(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId) || instanceId.StartsWith("Microsoft:Definition", StringComparison.OrdinalIgnoreCase))
            return null;
        var m = GuidRegex().Match(instanceId);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    /// <summary>Every GUID of an InstanceID, upper case, in order ("Microsoft:&lt;VM&gt;\&lt;adapter&gt;\…" → VM, adapter).</summary>
    public static List<string> Guids(string? instanceId)
        => string.IsNullOrEmpty(instanceId) ? []
         : GuidRegex().Matches(instanceId).Select(m => m.Value.ToUpperInvariant()).ToList();

    /// <summary>"00155D010203" → "00-15-5D-01-02-03" (as Get-NetAdapter shows it); other lengths unchanged.</summary>
    public static string FormatMac(string? mac)
    {
        var hex = NormalizeMac(mac);
        return hex.Length == 12 ? string.Join("-", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))) : mac ?? "";
    }

    /// <summary>Value of <paramref name="key"/> in a WMI object path (…,Name="x",…), null when absent.</summary>
    public static string? PathKey(string? path, string key)
    {
        if (path is null) return null;
        // The key starts the path's key list or follows a comma: "Name" must not match "CreationClassName"
        var m = Regex.Match(path, "[.,]" + Regex.Escape(key) + "=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value.Replace("\\\\", "\\") : null;
    }

    /// <summary>"00-15-5D-01-02-03", "00155D010203" → "00155D010203".</summary>
    public static string NormalizeMac(string? mac)
        => new string((mac ?? "").Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>First usable IPv4 address of a list (no APIPA 169.254.x.x, no IPv6).</summary>
    public static string? FirstIpv4(IEnumerable<string?>? addresses)
        => addresses?.FirstOrDefault(a => InputValidator.ParseIpv4(a) is not null && !a!.StartsWith("169.254."));

    /// <summary>VLAN column: access id, trunk list, or "" when untagged.</summary>
    public static string VlanText(long operationMode, long accessVlanId, IEnumerable<long>? trunk)
        => operationMode switch
        {
            1 when accessVlanId != 0 => accessVlanId.ToString(),
            2 => string.Join(",", (trunk ?? []).Where(v => v != 0)),
            _ => "",
        };

    /// <summary>Get-VMSwitch SwitchType from the switch's port allocations.</summary>
    public static string SwitchType(bool hasExternalPort, bool hasHostPort)
        => hasExternalPort ? "External" : hasHostPort ? "Internal" : "Private";

    /// <summary>
    /// Switch of a NAT network: the internal switch whose host adapter ("vEthernet (Name)") has an IPv4
    /// address inside the NAT prefix ("10.0.0.0/24"); "" when none matches.
    /// </summary>
    public static string NatSwitch(string? natPrefix, IEnumerable<(string InterfaceAlias, string Ip)> hostAddresses)
    {
        var parts = (natPrefix ?? "").Split('/');
        if (parts.Length != 2 || InputValidator.ParseIpv4(parts[0]) is not { } net || !int.TryParse(parts[1], out var len)
            || len is < 0 or > 32)
            return "";
        var mask = len == 0 ? 0u : uint.MaxValue << (32 - len);
        foreach (var (alias, ip) in hostAddresses)
            if (InputValidator.ParseIpv4(ip) is { } addr && (addr & mask) == (net & mask)
                && alias.StartsWith("vEthernet (", StringComparison.Ordinal) && alias.EndsWith(')'))
                return alias["vEthernet (".Length..^1];
        return "";
    }

    /// <summary>
    /// Checkpoints in tree order (parents first, siblings by creation time), with a "Now" row as the last
    /// child of the checkpoint the VM currently runs from.
    /// </summary>
    public static List<CheckpointRow> CheckpointTree(IReadOnlyCollection<VmCheckpoint> checkpoints, string? currentParentId)
    {
        var rows = new List<CheckpointRow>();
        if (checkpoints.Count == 0) return rows;
        var ids = checkpoints.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var children = checkpoints
            .GroupBy(c => c.ParentId is { } p && ids.Contains(p) ? p.ToUpperInvariant() : "")
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Created ?? DateTime.MinValue).ToList());

        void Walk(string parentKey, int depth)
        {
            if (children.TryGetValue(parentKey, out var list))
                foreach (var c in list)
                {
                    rows.Add(new CheckpointRow(c.Id, c.Name, c.Created, depth, false));
                    Walk(c.Id.ToUpperInvariant(), depth + 1);
                }
            if (parentKey.Length > 0 && string.Equals(parentKey, currentParentId, StringComparison.OrdinalIgnoreCase))
                rows.Add(new CheckpointRow("", "", null, depth, true));
        }
        Walk("", 0);
        return rows;
    }

    /// <summary>Thumbnail pixels: 16-bit RGB565, little endian, rows of width × 2 bytes → BGRA8 (opaque).</summary>
    public static byte[] Rgb565ToBgra(byte[] rgb565, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        var count = Math.Min(width * height, rgb565.Length / 2);
        for (int i = 0; i < count; i++)
        {
            int v = rgb565[2 * i] | (rgb565[2 * i + 1] << 8);
            int r = (v >> 11) & 0x1F, g = (v >> 5) & 0x3F, b = v & 0x1F;
            bgra[4 * i]     = (byte)((b * 527 + 23) >> 6);
            bgra[4 * i + 1] = (byte)((g * 259 + 33) >> 6);
            bgra[4 * i + 2] = (byte)((r * 527 + 23) >> 6);
            bgra[4 * i + 3] = 255;
        }
        return bgra;
    }

    /// <summary>Largest size within <paramref name="maxWidth"/>×<paramref name="maxHeight"/> with the guest screen's aspect ratio.</summary>
    public static (int Width, int Height) FitThumbnail(int screenWidth, int screenHeight, int maxWidth, int maxHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return (maxWidth, maxHeight);
        // Never larger than the guest screen: Hyper-V rejects an upscaled thumbnail (32775, invalid parameter)
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / screenWidth, (double)maxHeight / screenHeight));
        return (Math.Max(1, (int)(screenWidth * scale)), Math.Max(1, (int)(screenHeight * scale)));
    }
}
