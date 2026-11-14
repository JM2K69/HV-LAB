namespace HVLab.Models;

/// <summary>One network adapter of a VM (Msvm_SyntheticEthernetPortSettingData and its port allocation).</summary>
public sealed record VmNetworkAdapter(string VmId, string VmName, string VmState, string AdapterName, string MacAddress,
                                      string SwitchName, string Vlan, string IpAddress);

/// <summary>What a switch is connected to on the host side, drawn left of the switch in the topology view.</summary>
public enum UplinkKind { PhysicalAdapter, Nat, ConnectionSharing, HostOnly, Isolated }

/// <summary>A switch of the topology view: its uplink and the VM adapters plugged into it.</summary>
public sealed class TopologySwitch
{
    public string Name       { get; init; } = "";
    public string SwitchType { get; init; } = "";
    public UplinkKind Uplink { get; init; }
    /// <summary>Physical adapter, NAT network name, "Host"… (localized by the view model).</summary>
    public string UplinkTitle  { get; init; } = "";
    /// <summary>Adapter description, NAT prefix, host IP…</summary>
    public string UplinkDetail { get; init; } = "";
    public string HostAddress  { get; init; } = "";
    public string VlanText     { get; init; } = "";
    public IReadOnlyList<TopologyVm> Vms { get; init; } = [];

    public bool HasUplink => Uplink != UplinkKind.Isolated;
    public bool HasVms    => Vms.Count > 0;
    public bool HasNoVms  => Vms.Count == 0;
    /// <summary>"Internal · VLAN 10" under the switch name.</summary>
    public string TypeLine => VlanText.Length > 0 ? $"{SwitchType} · {VlanText}" : SwitchType;
    public string UplinkGlyph => Uplink switch
    {
        UplinkKind.PhysicalAdapter   => "",   // Ethernet
        UplinkKind.Nat               => "",   // Globe
        UplinkKind.ConnectionSharing => "",
        UplinkKind.HostOnly          => "",   // Desktop
        _                            => "",   // Lock
    };
}

/// <summary>Windows Internet connection sharing: the shared (public) and receiving (private) connections.</summary>
public sealed record IcsStatus(string PublicConnection, string PrivateConnection)
{
    /// <summary>Switch name when the private side is a Hyper-V host adapter ("vEthernet (LAB)" → "LAB").</summary>
    public string? SwitchName => PrivateConnection.StartsWith("vEthernet (", StringComparison.Ordinal) && PrivateConnection.EndsWith(')')
        ? PrivateConnection["vEthernet (".Length..^1] : null;
}

/// <summary>A VM adapter in the topology view.</summary>
public sealed record TopologyVm(string VmName, string State, string IpAddress, string Vlan, string AdapterName)
{
    public bool HasVlan => Vlan.Length > 0;
    public bool HasIp   => IpAddress.Length > 0;
    public string VlanLabel => $"VLAN {Vlan}";
}
