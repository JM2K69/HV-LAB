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
        };
    }

    // ─── Factory: generate filename from parts ────────────────────────────────

    /// <summary>
    /// Generate the canonical filename (without extension) from structured parts.
    /// BASE_{osIdentifier}_{osVersion}_{sizeGB}
    /// </summary>
    public static string BuildFileName(string osIdentifier, string osVersion, long sizeGB)
        => $"BASE_{osIdentifier}_{osVersion}_{sizeGB}";
}
