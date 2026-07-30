namespace HVLab.Models;

/// <summary>GVLK (KMS client setup key) from Microsoft Learn.</summary>
public record KmsKeyEntry(string Group, string OsName, string Key)
{
    /// <summary>Label shown in ComboBox: OS name only (group is shown as header).</summary>
    public string DisplayLabel => OsName;

    /// <summary>Full tooltip: OS + key.</summary>
    public string ToolTip => $"{OsName}  ·  {Key}";

    public override string ToString() => OsName;
}
