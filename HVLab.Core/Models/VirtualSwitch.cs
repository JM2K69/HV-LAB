namespace HVLab.Models;

public class VirtualSwitch
{
    /// <summary>Hyper-V switch GUID (Msvm_VirtualEthernetSwitch.Name): Hyper-V allows two switches with the same name.</summary>
    public string Id                { get; set; } = string.Empty;
    public string Name              { get; set; } = string.Empty;
    /// <summary>How the services address this switch: its GUID, else its name (which must then be unique).</summary>
    public string Target            => Id.Length > 0 ? Id : Name;
    public string SwitchType        { get; set; } = string.Empty;
    public string Notes             { get; set; } = string.Empty;
    public string NetAdapterName    { get; set; } = string.Empty;
    public bool   AllowManagementOS { get; set; }
    public int    VlanId            { get; set; }   // 0 = aucun VLAN

    public string VlanDisplay => VlanId > 0 ? VlanId.ToString() : "—";
}
