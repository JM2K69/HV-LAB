namespace HVLab.Models;

/// <summary>
/// Represents one index entry inside a WIM/ESD file as returned by Get-WindowsImage.
/// </summary>
public class WimImageInfo
{
    /// <summary>1-based index inside the WIM (matches the /Index parameter of DISM).</summary>
    public int    ImageIndex   { get; set; }

    /// <summary>Full image name, e.g. "Windows Server 2025 Standard (Desktop Experience)".</summary>
    public string ImageName    { get; set; } = string.Empty;

    /// <summary>Windows build version string, e.g. "10.0.26100.4652".</summary>
    public string Version      { get; set; } = string.Empty;

    /// <summary>Image size in bytes (uncompressed), as reported by DISM.</summary>
    public long   ImageSize    { get; set; }

    /// <summary>Friendly display string shown in the UI list.</summary>
    public string Display      => $"[{ImageIndex}] {ImageName}  ({Version})";

    // ── Edition matching helpers ─────────────────────────────────────────────

    /// <summary>
    /// Returns true when this WIM entry is the best match for the given
    /// HV-LAB edition key + desktop-experience flag.
    ///
    /// Matching rules (case-insensitive, applied to ImageName):
    ///   Edition key          WIM name must contain          WIM name must NOT contain
    ///   ──────────────────── ────────────────────────────── ─────────────────────────
    ///   Standard             "standard"                     "core" (unless key ends Core)
    ///   StandardCore         "standard"  AND "core"
    ///   Datacenter           "datacenter"                   "core"
    ///   DatacenterCore       "datacenter" AND "core"
    ///   Pro                  "pro"                          "education" / "workstation"
    ///   Enterprise           "enterprise"
    ///   Education            "education"
    ///   ProEducation         "pro" AND "education"          "workstation"
    ///   ProWorkstation       "workstation" OR "pro workstation"
    ///
    /// For server editions, desktopExperience=true additionally requires
    /// "(desktop experience)" in the name; false requires "core".
    /// </summary>
    public bool MatchesEdition(string editionKey, bool desktopExperience)
    {
        var n = ImageName.ToLowerInvariant();

        return editionKey.ToLowerInvariant() switch
        {
            "standard"           => n.Contains("standard") && !n.Contains("core"),
            "standardcore"       => n.Contains("standard") &&  n.Contains("core"),
            "datacenter"         => n.Contains("datacenter") && !n.Contains("core"),
            "datacentercore"     => n.Contains("datacenter") &&  n.Contains("core"),
            "pro"                => n.Contains("pro") && !n.Contains("education") && !n.Contains("workstation"),
            "enterprise"         => n.Contains("enterprise"),
            "education"          => n.Contains("education") && !n.Contains("pro"),
            "proeducation"       => n.Contains("pro") && n.Contains("education"),
            "proworkstation"     => n.Contains("workstation"),
            _ => false
        } && MatchesDesktopExperience(editionKey, desktopExperience, n);
    }

    private static bool MatchesDesktopExperience(string editionKey, bool desktopExp, string nameLower)
    {
        // Desktop Experience only applies to server (non-Core) editions
        bool isServer = editionKey is "Standard" or "Datacenter"
                     || editionKey.Equals("standard",   StringComparison.OrdinalIgnoreCase)
                     || editionKey.Equals("datacenter",  StringComparison.OrdinalIgnoreCase);

        if (!isServer) return true; // client editions don't distinguish

        if (desktopExp)
            return nameLower.Contains("desktop experience");
        else
            // core has already been handled by the outer switch; just pass through
            return true;
    }
}
