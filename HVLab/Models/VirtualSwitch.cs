namespace HVLab.Models;

public class VirtualSwitch
{
    public string Name { get; set; } = string.Empty;
    public string SwitchType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string NetAdapterName { get; set; } = string.Empty;
    public bool AllowManagementOS { get; set; }
}
