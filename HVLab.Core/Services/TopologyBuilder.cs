using HVLab.Models;
using static HVLab.Services.LocalizationService;

namespace HVLab.Services;

/// <summary>
/// Builds the topology view (unit-tested): each switch with its uplink on the left (physical adapter, NAT,
/// connection sharing, host only or isolated) and the VM adapters plugged into it on the right.
/// </summary>
public static class TopologyBuilder
{
    /// <summary>Host address Windows gives the private side of Internet connection sharing.</summary>
    public const string IcsHostAddress = "192.168.137.1";

    public static List<TopologySwitch> Build(IReadOnlyList<VirtualSwitch> switches, IReadOnlyList<NatNetwork> nats,
        IReadOnlyList<VmNetworkAdapter> adapters, IReadOnlyDictionary<string, string> hostAddresses)
    {
        // A VM with several adapters shows the adapter name next to each of its entries
        var multi = adapters.GroupBy(a => a.VmId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        TopologyVm Node(VmNetworkAdapter a) => new(a.VmName, a.VmState, a.IpAddress, a.Vlan, multi.Contains(a.VmId) ? a.AdapterName : "");

        var result = new List<TopologySwitch>();
        foreach (var sw in switches)
        {
            var host = hostAddresses.GetValueOrDefault($"vEthernet ({sw.Name})", "");
            var nat  = nats.FirstOrDefault(n => n.SwitchName == sw.Name);
            var (kind, title, detail) = sw.SwitchType switch
            {
                "External" => (UplinkKind.PhysicalAdapter, sw.NetAdapterName,
                               sw.AllowManagementOS && host.Length > 0 ? T("TOPO_HostIp", host) : ""),
                "Private"  => (UplinkKind.Isolated, T("TOPO_Isolated"), T("TOPO_IsolatedDetail")),
                _ when nat is not null => (UplinkKind.Nat, nat.Name, T("TOPO_NatDetail", nat.InternalIPInterfaceAddressPrefix, host)),
                _ when host == IcsHostAddress => (UplinkKind.ConnectionSharing, T("TOPO_Ics"), T("TOPO_IcsDetail")),
                _          => (UplinkKind.HostOnly, T("TOPO_Host"), host.Length > 0 ? T("TOPO_HostIp", host) : ""),
            };
            result.Add(new TopologySwitch
            {
                Name = sw.Name, SwitchType = sw.SwitchType, Uplink = kind, UplinkTitle = title, UplinkDetail = detail,
                HostAddress = host, VlanText = sw.VlanId > 0 ? $"VLAN {sw.VlanId}" : "",
                Vms = adapters.Where(a => a.SwitchName == sw.Name).Select(Node).ToList(),
            });
        }

        // Adapters plugged into nothing: drawn as one more, isolated group
        var loose = adapters.Where(a => a.SwitchName.Length == 0).Select(Node).ToList();
        if (loose.Count > 0)
            result.Add(new TopologySwitch
            {
                Name = T("TOPO_NotConnected"), Uplink = UplinkKind.Isolated,
                UplinkTitle = T("TOPO_Isolated"), UplinkDetail = "", Vms = loose,
            });
        return result;
    }
}
