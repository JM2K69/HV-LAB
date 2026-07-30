namespace HVLab.Models;

public class VirtualSwitch
{
    public string Name              { get; set; } = string.Empty;
    public string SwitchType        { get; set; } = string.Empty;
    public string Notes             { get; set; } = string.Empty;
    public string NetAdapterName    { get; set; } = string.Empty;
    public bool   AllowManagementOS { get; set; }
    public int    VlanId            { get; set; }   // 0 = aucun VLAN

    public string VlanDisplay => VlanId > 0 ? VlanId.ToString() : "—";
}
