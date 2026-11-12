using System.Text.RegularExpressions;

namespace HVLab.Models;

/// <summary>
/// Naming convention: BASE_{OsIdentifier}_{OsVersion}_{SizeGB}.vhdx
/// Example: BASE_WindowsServer2025Standard(DesktopExperience)_10.0.26100.4652_50.vhdx
/// </summary>
public class BaseVhdx
{
    // ─── Stored properties ───────────────────────────────────────────────────

    public string Name         { get; set; } = string.Empty;   // filename without extension
    public string FilePath     { get; set; } = string.Empty;
    public long   SizeGB       { get; set; }
    public bool   IsBuilt      { get; set; }
    public DateTime? CreatedAt { get; set; }

    // ─── Parsed segments ─────────────────────────────────────────────────────

    public string OsIdentifier { get; set; } = string.Empty;   // e.g. WindowsServer2025Standard(DesktopExperience)
    public string OsVersion    { get; set; } = string.Empty;   // e.g. 10.0.26100.4652

    // ─── Sidecar (bases made from a VM) ──────────────────────────────────────

    public BaseImageInfo? Info { get; set; }

    /// <summary>Linux base: its clones get a cloud-init seed instead of an unattend.xml.</summary>
    public bool   IsLinux            => Info?.IsLinux ?? OsIdentifier.StartsWith(LinuxPrefix, StringComparison.OrdinalIgnoreCase);
    public bool   SecureBoot         => Info?.SecureBoot ?? true;
    public string SecureBootTemplate => Info?.SecureBootTemplate
        ?? (IsLinux ? BaseImageInfo.LinuxSecureBootTemplate : BaseImageInfo.WindowsSecureBootTemplate);
    /// <summary>Generation of the VM the base was made from; null for bases built from a WIM.</summary>
    public int?   Generation         => Info?.Generation;

    // ─── Display helpers ─────────────────────────────────────────────────────

    public string SizeDisplay      => $"{SizeGB} GB";
    public string CreatedAtDisplay => CreatedAt?.ToString("dd/MM/yyyy HH:mm") ?? "—";
    public string StatusIcon       => IsBuilt ? "✅" : "⚠️";

    /// <summary>Human-friendly OS label stripped of camel-case separators.</summary>
    public string OsLabel => string.IsNullOrEmpty(OsIdentifier)
        ? Name
        : Regex.Replace(OsIdentifier, @"(?<=[a-z0-9])(?=[A-Z])", " ");

    // ─── Factory: parse filename ─────────────────────────────────────────────

    private static readonly Regex NamingPattern = new(
        @"^BASE_(?<os>.+)_(?<ver>\d+\.\d+\.\d+\.\d+)_(?<size>\d+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Build a BaseVhdx from a .vhdx file path.
    /// Parses the BASE_…_version_sizeGB naming convention when present;
    /// falls back to the raw filename for unrecognised files.
    /// </summary>
    public static BaseVhdx FromFile(string filePath)
    {
        var info     = new FileInfo(filePath);
        var stem     = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var match    = NamingPattern.Match(stem);

        long sizeGB;
        string osId, osVer;

        if (match.Success)
        {
            osId   = match.Groups["os"].Value;
            osVer  = match.Groups["ver"].Value;
            sizeGB = long.Parse(match.Groups["size"].Value);
        }
        else
        {
            osId   = string.Empty;
            osVer  = string.Empty;
            // Fallback: use actual file size (rounded)
            sizeGB = info.Exists ? info.Length / (1024L * 1024 * 1024) : 0;
        }

        return new BaseVhdx
        {
            Name         = stem,
            FilePath     = filePath,
            OsIdentifier = osId,
            OsVersion    = osVer,
            SizeGB       = sizeGB,
            IsBuilt      = true,
            CreatedAt    = info.Exists ? info.CreationTime : null,
            Info         = BaseImageInfo.Load(filePath),
        };
    }

    // ─── Linux bases ─────────────────────────────────────────────────────────

    private const string LinuxPrefix = "Linux";

    /// <summary>"AlmaLinux 9" → "LinuxAlmaLinux9": the OS segment of a Linux base file name.</summary>
    public static string LinuxIdentifier(string distribution)
        => LinuxPrefix + new string(distribution.Where(char.IsAsciiLetterOrDigit).ToArray());

    /// <summary>"9.6" → "9.6.0.0": the four numbers the file name expects; null when it is not a version.</summary>
    public static string? NormalizeVersion(string version)
    {
        var parts = version.Trim().Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit))) return null;
        return string.Join('.', parts.Concat(Enumerable.Repeat("0", 4 - parts.Length)));
    }

    // ─── Factory: generate filename from parts ────────────────────────────────

    /// <summary>
    /// Generate the canonical filename (without extension) from structured parts.
    /// BASE_{osIdentifier}_{osVersion}_{sizeGB}
    /// </summary>
    public static string BuildFileName(string osIdentifier, string osVersion, long sizeGB)
        => $"BASE_{osIdentifier}_{osVersion}_{sizeGB}";
}
