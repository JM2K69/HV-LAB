namespace HVLab.Models;

/// <summary>
/// KMS client setup key (GVLK) from Microsoft Learn. Home editions have no GVLK:
/// their entries are generic install keys (no activation) and are flagged as such.
/// </summary>
public record KmsKeyEntry(string Group, string OsName, string Key)
{
    /// <summary>True for generic (non-KMS) install keys, i.e. Home editions.</summary>
    public bool IsGeneric => OsName.StartsWith("Home", StringComparison.OrdinalIgnoreCase);

    /// <summary>Label shown in ComboBox: OS name only (group is shown as header).</summary>
    public string DisplayLabel => IsGeneric ? $"{OsName} ({HVLab.Services.LocalizationService.Instance["KMS_GenericKey"]})" : OsName;

    /// <summary>Full tooltip: OS + key.</summary>
    public string ToolTip => $"{OsName}  ·  {Key}";

    public override string ToString() => OsName;
}
