namespace HVLab.Models;

public class NetworkAdapterInfo
{
    public string Name               { get; set; } = string.Empty;
    public string Description        { get; set; } = string.Empty;
    public string MacAddress         { get; set; } = string.Empty;
    public string Status             { get; set; } = string.Empty;

    /// <summary>Display label shown in the ComboBox: Name — Description</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Description)
        ? Name
        : $"{Name}  —  {Description}";

    public override string ToString() => DisplayLabel;
}
