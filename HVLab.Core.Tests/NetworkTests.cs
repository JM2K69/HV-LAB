using HVLab.Models;
using HVLab.Services;
using HVLab.Services.Wmi;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

internal sealed class FakeNatService : INatService
{
    public List<NatNetwork> Nats { get; set; } = [];
    public Task<List<NatNetwork>> GetNatNetworksAsync() => Task.FromResult(Nats.ToList());
    public Task CreateNatNetworkAsync(string switchName, string natName, string gatewayIP, int prefixLength) => Task.CompletedTask;
    public Task RemoveNatNetworkAsync(string name) => Task.CompletedTask;
}

public class TopologyTests
{
    private static VmNetworkAdapter Nic(string vm, string sw, string ip = "", string vlan = "", string id = "", string adapter = "Network Adapter")
        => new(id.Length > 0 ? id : vm, vm, "Running", adapter, "00-15-5D-00-00-01", sw, vlan, ip);

    [Fact]
    public void Each_switch_gets_its_uplink_and_its_vms()
    {
        VirtualSwitch[] switches =
        [
            new() { Name = "LAN", SwitchType = "External", NetAdapterName = "Ethernet", AllowManagementOS = true },
            new() { Name = "LAB", SwitchType = "Internal" },
            new() { Name = "ICS", SwitchType = "Internal" },
            new() { Name = "MGMT", SwitchType = "Internal", VlanId = 10 },
            new() { Name = "ISO", SwitchType = "Private" },
        ];
        NatNetwork[] nats = [new() { Name = "NAT-LAB", InternalIPInterfaceAddressPrefix = "192.168.100.0/24", SwitchName = "LAB" }];
        var hosts = new Dictionary<string, string>
        {
            ["vEthernet (LAN)"] = "192.168.1.20", ["vEthernet (LAB)"] = "192.168.100.1",
            ["vEthernet (ICS)"] = TopologyBuilder.IcsHostAddress, ["vEthernet (MGMT)"] = "10.10.0.1",
        };
        VmNetworkAdapter[] nics = [Nic("SRV-02", "LAB", "192.168.100.11"), Nic("SRV-01", "LAB", "192.168.100.10", "20"), Nic("CLI", "")];

        var topo = TopologyBuilder.Build(switches, nats, nics, hosts);

        Assert.Equal(["LAN", "LAB", "ICS", "MGMT", "ISO", LocalizationService.T("TOPO_NotConnected")], topo.Select(t => t.Name));
        Assert.Equal(UplinkKind.PhysicalAdapter, topo[0].Uplink);
        Assert.Equal("Ethernet", topo[0].UplinkTitle);
        Assert.Equal(UplinkKind.Nat, topo[1].Uplink);
        Assert.Equal("NAT-LAB", topo[1].UplinkTitle);
        Assert.Contains("192.168.100.0/24", topo[1].UplinkDetail);
        Assert.Equal(["SRV-02", "SRV-01"], topo[1].Vms.Select(v => v.VmName));   // adapter order kept
        Assert.Equal("VLAN 20", topo[1].Vms[1].VlanLabel);
        Assert.Equal(UplinkKind.ConnectionSharing, topo[2].Uplink);
        Assert.Equal(UplinkKind.HostOnly, topo[3].Uplink);
        Assert.Equal("Internal · VLAN 10", topo[3].TypeLine);
        Assert.Equal(UplinkKind.Isolated, topo[4].Uplink);
        Assert.False(topo[4].HasUplink);
        Assert.True(topo[4].HasNoVms);
        Assert.Equal("CLI", Assert.Single(topo[5].Vms).VmName);
    }

    [Fact]
    public void Adapter_names_show_only_for_vms_with_several_adapters()
    {
        VirtualSwitch[] switches = [new() { Name = "A", SwitchType = "Private" }, new() { Name = "B", SwitchType = "Private" }];
        VmNetworkAdapter[] nics = [Nic("R1", "A", id: "1", adapter: "WAN"), Nic("R1", "B", id: "1", adapter: "LAN"), Nic("PC", "B", id: "2")];
        var topo = TopologyBuilder.Build(switches, [], nics, new Dictionary<string, string>());
        Assert.Equal("WAN", topo[0].Vms[0].AdapterName);
        Assert.Equal(["LAN", ""], topo[1].Vms.Select(v => v.AdapterName));
    }

    [Fact]
    public void Ics_status_finds_its_switch()
    {
        Assert.Equal("LAB", new IcsStatus("Wi-Fi", "vEthernet (LAB)").SwitchName);
        Assert.Null(new IcsStatus("Wi-Fi", "Ethernet 2").SwitchName);
    }
}

public class NetworkWmiMapTests
{
    [Fact]
    public void Guids_and_mac_formats()
    {
        Assert.Equal(["1FB0D232-A460-4A47-80B1-D5E9110FC7F8", "D4FEFC6F-A15F-49BD-99C7-12DB673217DB"],
            WmiMap.Guids(@"Microsoft:GuestNetwork\1fb0d232-a460-4a47-80b1-d5e9110fc7f8\D4FEFC6F-A15F-49BD-99C7-12DB673217DB"));
        Assert.Empty(WmiMap.Guids(null));
        Assert.Equal("00-15-5D-01-02-03", WmiMap.FormatMac("00155D010203"));
        Assert.Equal("", WmiMap.FormatMac(""));
    }
}

public class VlanSettingsTests
{
    [Theory]
    [InlineData("10", true)]
    [InlineData("10,20,100-110", true)]
    [InlineData("10, 20", true)]
    [InlineData("0", false)]
    [InlineData("4095", false)]
    [InlineData("20-10", false)]
    [InlineData("10,,20", false)]
    [InlineData("", false)]
    public void Allowed_vlan_lists(string list, bool valid) => Assert.Equal(valid, VmSettings.IsValidVlanList(list));

    [Fact]
    public void Trunk_and_guards_are_applied_on_the_first_adapter()
    {
        var before = new VmSettings();
        var after = before.Clone();
        after.VlanMode = VmSettings.VlanTrunk;
        after.NativeVlanId = 1;
        after.AllowedVlans = "10, 20-30";
        after.DhcpGuard = true;
        after.RouterGuard = true;
        Assert.Null(after.Validate());
        var lines = HyperVService.SettingsScript(before, after);
        Assert.Contains(lines, l => l.Contains("-Trunk -NativeVlanId 1 -AllowedVlanIdList '10,20-30'"));
        Assert.Contains(lines, l => l.Contains("Set-VMNetworkAdapter -DhcpGuard On -RouterGuard On"));

        after.AllowedVlans = "5000";
        Assert.Equal("Val_VmTrunk", after.Validate());
        after.VlanMode = VmSettings.VlanAccess;
        after.VlanId = 0;
        Assert.Equal("Val_VmVlan", after.Validate());
    }

    [Fact]
    public void Nested_virtualization_turns_mac_spoofing_on()
    {
        var s = new VmSettings();
        s.Nested = true;
        Assert.True(s.MacSpoofing);
        Assert.False(s.DynamicMemory);
    }
}

public class SwitchViewModelTests
{
    private static (VirtualSwitchesViewModel Vm, FakeHyperVService Hv, FakeNatService Nat, FakeDialogService Dialogs) Create()
    {
        var hv = new FakeHyperVService
        {
            Switches = [new VirtualSwitch { Name = "LAB", SwitchType = "Internal" }, new VirtualSwitch { Name = "LAN", SwitchType = "External", NetAdapterName = "Ethernet" }],
            PhysicalAdapters = [new NetworkAdapterInfo { Name = "Ethernet" }, new NetworkAdapterInfo { Name = "Wi-Fi" }],
            VmAdapters = [new VmNetworkAdapter("1", "SRV-01", "Running", "Network Adapter", "", "LAB", "", "192.168.100.10")],
            HostAddresses = new() { ["vEthernet (LAB)"] = "192.168.100.1" },
        };
        var nat = new FakeNatService { Nats = [new NatNetwork { Name = "NAT-LAB", InternalIPInterfaceAddressPrefix = "192.168.100.0/24", SwitchName = "LAB" }] };
        var dialogs = new FakeDialogService();
        return (new VirtualSwitchesViewModel(hv, nat, dialogs), hv, nat, dialogs);
    }

    [Fact]
    public async Task Refresh_builds_the_topology()
    {
        var (vm, _, _, _) = Create();
        await vm.RefreshAsync();
        Assert.Equal(["LAB", "LAN"], vm.Topology.Select(t => t.Name));
        Assert.Equal(UplinkKind.Nat, vm.Topology[0].Uplink);
        Assert.Equal("SRV-01", Assert.Single(vm.Topology[0].Vms).VmName);
    }

    [Fact]
    public async Task Changing_a_switch_type_is_validated_and_warns_about_its_nat()
    {
        var (vm, hv, _, _) = Create();
        await vm.RefreshAsync();
        vm.BeginChangeType(vm.VirtualSwitches[0]);
        Assert.NotNull(vm.ValidateChangeType());                 // unchanged
        vm.EditSwitchType = "Private";
        Assert.Contains("NAT-LAB", vm.ChangeTypeWarning);
        vm.EditSwitchType = "External";
        vm.EditSwitchAdapter = null;
        Assert.NotNull(vm.ValidateChangeType());                 // no adapter
        vm.EditSwitchAdapter = vm.NetworkAdapters[1];
        Assert.Null(vm.ValidateChangeType());
        await vm.ApplyChangeTypeAsync();
        Assert.Contains("switch-type LAB External Wi-Fi", hv.Calls);
    }

    [Fact]
    public async Task Connection_sharing_refuses_a_switch_that_has_a_nat()
    {
        var (vm, hv, _, _) = Create();
        await vm.RefreshAsync();
        vm.IcsPublicAdapter = vm.NetworkAdapters[1];
        vm.IcsSwitch = "LAB";
        await vm.EnableIcsCommand.ExecuteAsync(null);
        Assert.DoesNotContain(hv.Calls, c => c.StartsWith("ics-on"));

        hv.Switches.Add(new VirtualSwitch { Name = "DHCP", SwitchType = "Internal" });
        await vm.RefreshAsync();
        vm.IcsSwitch = "DHCP";
        await vm.EnableIcsCommand.ExecuteAsync(null);
        Assert.Contains("ics-on Wi-Fi DHCP", hv.Calls);
        Assert.True(vm.IsIcsOn);
        await vm.DisableIcsCommand.ExecuteAsync(null);
        Assert.False(vm.IsIcsOn);
    }
}

public class NetworkScriptTests
{
    [Fact]
    public async Task Switch_type_change_rolls_back_on_failure()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).ChangeSwitchTypeAsync("LAB", "External", "Wi-Fi");
        var script = runner.Scripts.Single();
        Assert.Contains("Set-VMSwitch -Name $name -NetAdapterName $adapter -AllowManagementOS $true", script);
        Assert.Contains("Restore-HostIps", script);
        Assert.Contains("throw $failure", script);
        await Assert.ThrowsAsync<ArgumentException>(() => new HyperVService(runner).ChangeSwitchTypeAsync("LAB", "Bogus", null));
        await Assert.ThrowsAsync<ArgumentException>(() => new HyperVService(runner).ChangeSwitchTypeAsync("LAB", "External", null));
    }

    [Fact]
    public async Task Connection_sharing_uses_hnetcfg_public_then_private()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).EnableIcsAsync("Wi-Fi", "LAB");
        var script = runner.Scripts.Single();
        Assert.Contains("HNetCfg.HNetShare", script);
        Assert.Contains("'vEthernet (LAB)'", script);
        Assert.True(script.IndexOf("EnableSharing(0)", StringComparison.Ordinal) < script.IndexOf("EnableSharing(1)", StringComparison.Ordinal));
    }
}
