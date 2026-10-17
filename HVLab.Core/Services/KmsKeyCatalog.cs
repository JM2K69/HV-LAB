using HVLab.Models;

namespace HVLab.Services;

/// <summary>
/// GVLK keys sourced from Microsoft Learn:
/// https://learn.microsoft.com/windows-server/get-started/kms-client-activation-keys
/// </summary>
public static class KmsKeyCatalog
{
    public static readonly IReadOnlyList<KmsKeyEntry> All = new List<KmsKeyEntry>
    {
        // ── Windows Server 2025 ─────────────────────────────────────────────
        new("Windows Server 2025",    "Standard",                           "TVRH6-WHNXV-R9WG3-9XRFY-MY832"),
        new("Windows Server 2025",    "Datacenter",                         "D764K-2NDRG-47T6Q-P8T8W-YP6DF"),
        new("Windows Server 2025",    "Datacenter: Azure Edition",          "XGN3F-F394H-FD2MY-PP6FD-8MCRC"),

        // ── Windows Server 2022 ─────────────────────────────────────────────
        new("Windows Server 2022",    "Standard",                           "VDYBN-27WPP-V4HQT-9VMD4-VMK7H"),
        new("Windows Server 2022",    "Datacenter",                         "WX4NM-KYWYW-QJJR4-XV3QB-6VM33"),
        new("Windows Server 2022",    "Datacenter: Azure Edition",          "NTBV8-9K7Q8-V27C6-M2BTV-KHMXV"),

        // ── Windows Server 2019 ─────────────────────────────────────────────
        new("Windows Server 2019",    "Standard",                           "N69G4-B89J2-4G8F4-WWYCC-J464C"),
        new("Windows Server 2019",    "Datacenter",                         "WMDGN-G9PQG-XVVXX-R3X43-63DFG"),
        new("Windows Server 2019",    "Essentials",                         "WVDHN-86M7X-466P6-VHXV7-YY726"),

        // ── Windows Server 2016 ─────────────────────────────────────────────
        new("Windows Server 2016",    "Standard",                           "WC2BQ-8NRM3-FDDYY-2BFGV-KHKQY"),
        new("Windows Server 2016",    "Datacenter",                         "CB7KF-BWN84-R7R2Y-793K2-8XDDG"),
        new("Windows Server 2016",    "Essentials",                         "JCKRF-N37P4-C2D82-9YXRT-4M63B"),

        // ── Windows Server 2012 R2 ──────────────────────────────────────────
        new("Windows Server 2012 R2", "Standard",                           "D2N9P-3P6X9-2R39C-7RTCD-MDVJX"),
        new("Windows Server 2012 R2", "Datacenter",                         "W3GGN-FT8W3-Y4M27-J84CP-Q3VJ9"),
        new("Windows Server 2012 R2", "Essentials",                         "KNC87-3J2TX-XB4WP-VCPJV-M4FWM"),

        // ── Windows 11 ──────────────────────────────────────────────────────
        new("Windows 11",             "Home",                               "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99"),
        new("Windows 11",             "Home N",                             "3KHY7-WNT83-DGQKR-F7HPR-844BM"),
        new("Windows 11",             "Home Single Language",               "7HNRX-D7KGG-3K4RQ-4WPJ4-YTDFH"),
        new("Windows 11",             "Pro",                                "W269N-WFGWX-YVC9B-4J6C9-T83GX"),
        new("Windows 11",             "Pro N",                              "MH37W-N47XK-V7XM9-C7227-GCQG9"),
        new("Windows 11",             "Pro Education",                      "6TP4R-GNPTD-KYYHQ-7B7DP-J447Y"),
        new("Windows 11",             "Pro Education N",                    "YVWGF-BXNMC-HTQYQ-CPQ99-66QFC"),
        new("Windows 11",             "Pro for Workstations",               "NRG8B-VKK3Q-CXVCJ-9G2XF-6Q84J"),
        new("Windows 11",             "Pro for Workstations N",             "9FNHH-K3HBT-3W4TD-6383H-6XYWF"),
        new("Windows 11",             "Enterprise",                         "NPPR9-FWDCX-D2C8J-H872K-2YT43"),
        new("Windows 11",             "Enterprise N",                       "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4"),
        new("Windows 11",             "Enterprise G",                       "YYVX9-NTFWV-6MDM3-9PT4T-4M68B"),
        new("Windows 11",             "Education",                          "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2"),
        new("Windows 11",             "Education N",                        "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ"),
        new("Windows 11",             "IoT Enterprise",                     "XQQYW-NFFMW-XJPNQ-3YAUP-8WWE8"),
        new("Windows 11",             "IoT Enterprise LTSC 2024",           "CGK42-GYN6Y-VD22B-BX98W-J9KGG"),
        new("Windows 11",             "Enterprise LTSC 2024",               "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"),
        new("Windows 11",             "Enterprise N LTSC 2024",             "92NFX-8DJQP-P6BBQ-THF9C-7CG2H"),
        new("Windows 11",             "Enterprise LTSC 2021",               "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"),
        new("Windows 11",             "Enterprise N LTSC 2021",             "92NFX-8DJQP-P6BBQ-THF9C-7CG2H"),

        // ── Windows 10 ──────────────────────────────────────────────────────
        new("Windows 10",             "Home",                               "TX9XD-98N7V-6WMQ6-BX7FG-H8Q99"),
        new("Windows 10",             "Pro",                                "W269N-WFGWX-YVC9B-4J6C9-T83GX"),
        new("Windows 10",             "Pro N",                              "MH37W-N47XK-V7XM9-C7227-GCQG9"),
        new("Windows 10",             "Enterprise",                         "NPPR9-FWDCX-D2C8J-H872K-2YT43"),
        new("Windows 10",             "Enterprise N",                       "DPH2V-TTNVB-4X9Q3-TJR4H-KHJW4"),
        new("Windows 10",             "Enterprise G",                       "YYVX9-NTFWV-6MDM3-9PT4T-4M68B"),
        new("Windows 10",             "Education",                          "NW6C2-QMPVW-D7KKK-3GKT6-VCFB2"),
        new("Windows 10",             "Education N",                        "2WH4N-8QGBV-H22JP-CT43Q-MDWWJ"),
        new("Windows 10",             "Enterprise LTSB 2016",               "DCPHK-NFMTC-H88MJ-PFHPY-QJ4BJ"),
        new("Windows 10",             "Enterprise N LTSB 2016",             "QFFDN-GRT3P-VKWWX-X7T3R-8B639"),
        new("Windows 10",             "Enterprise LTSC 2019",               "M7XTQ-FN8P6-TTKYV-9D4CC-J462D"),
        new("Windows 10",             "Enterprise N LTSC 2019",             "92NFX-8DJQP-P6BBQ-THF9C-7CG2H"),
    };

    /// <summary>Returns keys grouped by OS family, sorted Server first then client.</summary>
    public static IEnumerable<IGrouping<string, KmsKeyEntry>> Grouped()
        => All.GroupBy(k => k.Group);

    /// <summary>
    /// Suggests the best matching KMS key from a BaseVhdx.OsIdentifier string.
    /// Examples of identifiers:
    ///   "WindowsServer2025Standard(DesktopExperience)"
    ///   "WindowsServer2022Datacenter"
    ///   "Windows11Pro"
    ///   "Windows10Enterprise"
    /// Returns null if no match found.
    /// </summary>
    public static KmsKeyEntry? SuggestForOs(string? osIdentifier)
    {
        if (string.IsNullOrWhiteSpace(osIdentifier)) return null;

        // Normalize: lowercase, remove spaces/parentheses/hyphens for fuzzy matching
        var norm = osIdentifier.ToLowerInvariant()
                               .Replace("(", "").Replace(")", "")
                               .Replace(" ", "").Replace("-", "");

        // ── Determine OS group ───────────────────────────────────────────────
        string? group = norm switch
        {
            var s when s.Contains("server2025") => "Windows Server 2025",
            var s when s.Contains("server2022") => "Windows Server 2022",
            var s when s.Contains("server2019") => "Windows Server 2019",
            var s when s.Contains("server2016") => "Windows Server 2016",
            var s when s.Contains("server2012r2") || s.Contains("server2012 r2") => "Windows Server 2012 R2",
            var s when s.Contains("windows11") || s.Contains("win11") => "Windows 11",
            var s when s.Contains("windows10") || s.Contains("win10") => "Windows 10",
            _ => null
        };

        if (group is null) return null;

        var candidates = All.Where(k => k.Group == group).ToList();

        // ── Determine edition (priority order: most specific first) ──────────
        string? edition = norm switch
        {
            var s when s.Contains("datacentercoreazure") || s.Contains("datacenterazure") => "Datacenter: Azure Edition",
            var s when s.Contains("datacentercore") || s.Contains("datacenternano") => "Datacenter",
            var s when s.Contains("datacenter")    => "Datacenter",
            var s when s.Contains("standardcore") || s.Contains("standardnano") => "Standard",
            var s when s.Contains("standard")      => "Standard",
            var s when s.Contains("essentials")    => "Essentials",
            var s when s.Contains("enterprise") && s.Contains("ltsc2024") => "Enterprise LTSC 2024",
            var s when s.Contains("enterprise") && s.Contains("ltsc2021") => "Enterprise LTSC 2021",
            var s when s.Contains("enterprise") && s.Contains("ltsc2019") => "Enterprise LTSC 2019",
            var s when s.Contains("enterprise") && s.Contains("ltsb2016") => "Enterprise LTSB 2016",
            var s when s.Contains("enterpriseg")   => "Enterprise G",
            var s when s.Contains("enterprisen")   => "Enterprise N",
            var s when s.Contains("enterprise")    => "Enterprise",
            var s when s.Contains("proeducationn") => "Pro Education N",
            var s when s.Contains("proeducation")  => "Pro Education",
            var s when s.Contains("proworkstationsn") || (s.Contains("pro") && s.Contains("workstation") && s.Contains("n")) => "Pro for Workstations N",
            var s when s.Contains("proworkstations") || (s.Contains("pro") && s.Contains("workstation")) => "Pro for Workstations",
            var s when s.Contains("pron")          => "Pro N",
            var s when s.Contains("pro")           => "Pro",
            var s when s.Contains("educationn")    => "Education N",
            var s when s.Contains("education")     => "Education",
            var s when s.Contains("homen")         => "Home N",
            var s when s.Contains("homesinglelanguage") => "Home Single Language",
            var s when s.Contains("home")          => "Home",
            _                                      => null
        };

        if (edition is not null)
        {
            var exact = candidates.FirstOrDefault(k =>
                string.Equals(k.OsName, edition, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        // Fallback: return the first candidate of the group (Standard / Pro)
        return candidates.FirstOrDefault();
    }
}
