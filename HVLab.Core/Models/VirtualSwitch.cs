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
    /// <summary>Access VLAN of each host card plugged into the switch (0 = untagged), lowest first.</summary>
    public IReadOnlyList<int> HostVlans { get; set; } = [];

    /// <summary>"100", or "—, 100, 200" when the host has several cards on the switch (one per VLAN).</summary>
    public string VlanDisplay => HostVlans.Count > 1
        ? string.Join(", ", HostVlans.Select(v => v > 0 ? v.ToString() : "—"))
        : VlanId > 0 ? VlanId.ToString() : "—";
}
