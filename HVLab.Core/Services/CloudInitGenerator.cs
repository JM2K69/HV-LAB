using System.Text;

namespace HVLab.Services;

/// <summary>Static IPv4 settings of a clone; null in <see cref="CloudInitConfig.StaticIp"/> means DHCP.</summary>
public sealed record StaticIpConfig(string Address, int PrefixLength, string Gateway, IReadOnlyList<string> DnsServers);

public class CloudInitConfig
{
    public string HostName     { get; set; } = "lab-vm";
    /// <summary>cloud-init runs its first-boot modules once per instance-id: unique per clone.</summary>
    public string InstanceId   { get; set; } = Guid.NewGuid().ToString();
    public string UserName     { get; set; } = "labadmin";
    public string Password     { get; set; } = "";
    public string SshPublicKey { get; set; } = "";
    /// <summary>Windows time zone id (the list shared with the Windows answer file), converted to IANA.</summary>
    public string TimeZone     { get; set; } = "Romance Standard Time";
    /// <summary>UI language of the form (fr-FR…), which gives the keyboard layout.</summary>
    public string Language     { get; set; } = "fr-FR";
    public StaticIpConfig? StaticIp { get; set; }
}

/// <summary>Files of a NoCloud seed (volume labelled CIDATA); NetworkConfig is null for DHCP.</summary>
public sealed record CloudInitSeed(string MetaData, string UserData, string? NetworkConfig)
{
    public IEnumerable<KeyValuePair<string, string>> Files()
    {
        yield return new("meta-data", MetaData);
        yield return new("user-data", UserData);
        if (NetworkConfig is not null) yield return new("network-config", NetworkConfig);
    }
}

/// <summary>
/// cloud-init NoCloud seed for the clones of a Linux base: host name, one sudo user with its password
/// (and an optional SSH key), time zone, keyboard, and DHCP or a static IPv4 on eth0, the name the
/// Hyper-V synthetic adapter gets. Values are validated by <see cref="InputValidator"/> beforehand.
/// </summary>
public static class CloudInitGenerator
{
    public static CloudInitSeed Generate(CloudInitConfig c)
    {
        var host = c.HostName.ToLowerInvariant();
        var meta = $"instance-id: {Q(c.InstanceId)}\nlocal-hostname: {host}\n";

        var user = new StringBuilder()
            .Append("#cloud-config\n")
            .Append($"hostname: {host}\n")
            .Append("preserve_hostname: false\n")
            .Append("users:\n")
            .Append($"  - name: {c.UserName}\n")
            // A sudo rule rather than a group: wheel (RHEL) and sudo (Debian) differ between distributions
            .Append("    sudo: \"ALL=(ALL) NOPASSWD:ALL\"\n")
            .Append("    shell: /bin/bash\n")
            .Append("    lock_passwd: false\n");
        if (!string.IsNullOrWhiteSpace(c.SshPublicKey))
            user.Append("    ssh_authorized_keys:\n").Append($"      - {Q(c.SshPublicKey.Trim())}\n");
        user.Append("chpasswd:\n")
            .Append("  expire: false\n")
            .Append("  users:\n")
            .Append($"    - name: {c.UserName}\n")
            .Append($"      password: {Q(c.Password)}\n")
            .Append("      type: text\n")
            .Append("ssh_pwauth: true\n");
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(c.TimeZone, out var iana))
            user.Append($"timezone: {iana}\n");
        user.Append($"keyboard:\n  layout: {KeyboardLayout(c.Language)}\n");

        return new CloudInitSeed(meta, user.ToString(), c.StaticIp is { } ip ? NetworkConfig(ip) : null);
    }

    private static string NetworkConfig(StaticIpConfig ip)
    {
        var net = new StringBuilder()
            .Append("version: 2\n")
            .Append("ethernets:\n")
            .Append("  eth0:\n")
            .Append("    dhcp4: false\n")
            .Append($"    addresses:\n      - {ip.Address}/{ip.PrefixLength}\n")
            .Append($"    routes:\n      - to: 0.0.0.0/0\n        via: {ip.Gateway}\n");
        if (ip.DnsServers.Count > 0)
        {
            net.Append("    nameservers:\n      addresses:\n");
            foreach (var dns in ip.DnsServers) net.Append($"        - {dns}\n");
        }
        return net.ToString();
    }

    /// <summary>Console keyboard layout of the form language (fr-FR → fr, en-US → us…).</summary>
    public static string KeyboardLayout(string language) => language switch
    {
        "en-US" => "us",
        "en-GB" => "gb",
        _ when language.Length >= 2 => language[..2].ToLowerInvariant(),
        _ => "us",
    };

    /// <summary>Double-quoted YAML scalar: the password and the SSH key may hold any character.</summary>
    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The three files as shown in the Create VM preview.</summary>
    public static string Preview(CloudInitSeed seed)
        => string.Join("\n", seed.Files().Select(f => $"# ── {f.Key} ──\n{f.Value}"));
}
