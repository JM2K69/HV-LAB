using static HVLab.Services.LocalizationService;

namespace HVLab.Models;

/// <summary>
/// A network card of the host plugged into a switch (Get-VMNetworkAdapter -ManagementOS), "vEthernet (Name)" in
/// Windows. Several cards on one external switch, each on its own VLAN, give the host one interface per VLAN.
/// </summary>
/// <param name="Id">Hyper-V id of the card: what the changes target (two cards may share a name).</param>
/// <param name="VlanMode">Untagged, Access or Trunk (Get-VMNetworkAdapterVlan).</param>
/// <param name="Alias">Windows interface name, empty when Windows has not shown it yet.</param>
public sealed record HostAdapter(string Id, string Name, string SwitchName, string SwitchId, string VlanMode, int VlanId,
                                 string Alias, string Status, string IPv4, int PrefixLength, bool Dhcp)
{
    public string VlanDisplay => VlanMode.Equals("Trunk", StringComparison.OrdinalIgnoreCase) ? "trunk"
                               : VlanId > 0 ? VlanId.ToString() : T("HOSTNIC_Untagged");
    public string IpDisplay   => IPv4.Length > 0 ? $"{IPv4}/{PrefixLength}" : "—";
    public string DhcpDisplay => T(Dhcp ? "HOSTNIC_Dhcp" : "HOSTNIC_NoDhcp");
}
