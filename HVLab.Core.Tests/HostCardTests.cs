using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

/// <summary>Host network cards on a switch, one per VLAN (Add-VMNetworkAdapter -ManagementOS).</summary>
public class HostCardTests
{
    private static HostAdapter Card(string name, int vlan, bool dhcp = true, string sw = "LAN")
        => new($"Microsoft:{name}", name, sw, "SW-ID", vlan > 0 ? "Access" : "Untagged", vlan,
               $"vEthernet ({name})", "Up", vlan > 0 ? "10.0.100.5" : "", 24, dhcp);

    [Fact]
    public void A_switch_shows_every_vlan_of_its_host_cards()
    {
        var sw = new VirtualSwitch { Name = "LAN", SwitchType = "External", AllowManagementOS = true, HostVlans = [0, 100, 200] };
        Assert.Equal("—, 100, 200", sw.VlanDisplay);
        Assert.Equal("VLAN 100, 200", TopologyBuilder.Build([sw], [], [], new Dictionary<string, string>()).Single().VlanText);

        var one = new VirtualSwitch { Name = "LAB", SwitchType = "Internal", VlanId = 10, HostVlans = [10] };
        Assert.Equal("10", one.VlanDisplay);
        Assert.Equal("—", new VirtualSwitch { Name = "X", HostVlans = [0] }.VlanDisplay);
    }

    [Fact]
    public void Host_cards_are_read_from_powershell_json()
    {
        Assert.Empty(HyperVService.ParseHostAdapters("[]"));
        var single = HyperVService.ParseHostAdapters(
            """{"Id":"Microsoft:A","Name":"LAN-VLAN100","SwitchName":"LAN","SwitchId":"S","VlanMode":"Access","VlanId":100,"Alias":"vEthernet (LAN-VLAN100)","Status":"Up","IPv4":"10.0.100.5","PrefixLength":24,"Dhcp":false}""");
        var card = Assert.Single(single);
        Assert.Equal(("LAN-VLAN100", 100, "10.0.100.5/24", false), (card.Name, card.VlanId, card.IpDisplay, card.Dhcp));
        Assert.Equal(LocalizationService.T("HOSTNIC_NoDhcp"), card.DhcpDisplay);

        var two = HyperVService.ParseHostAdapters(
            """[{"Id":"B","Name":"LAN-VLAN200","SwitchName":"LAN","VlanMode":"Access","VlanId":200,"Dhcp":true},{"Id":"A","Name":"LAN","SwitchName":"LAN","VlanMode":"Untagged","VlanId":0,"Dhcp":true}]""");
        Assert.Equal(["LAN", "LAN-VLAN200"], two.Select(c => c.Name));          // untagged first, then by VLAN
        Assert.Equal(LocalizationService.T("HOSTNIC_Untagged"), two[0].VlanDisplay);
        Assert.Equal("—", two[0].IpDisplay);
    }

    [Theory]
    [InlineData("LAN-VLAN100", true)]
    [InlineData("Réseau 2.prod", true)]
    [InlineData("", false)]
    [InlineData(" LAN", false)]
    [InlineData("LAN'1", false)]
    [InlineData("LAN (100)", false)]
    public void Host_card_names(string name, bool valid) => Assert.Equal(valid, InputValidator.CheckHostAdapterName(name) is null);

    private static (VirtualSwitchesViewModel Vm, FakeHyperVService Hv, FakeDialogService Dialogs) Page()
    {
        var hv = new FakeHyperVService
        {
            Switches = [new VirtualSwitch { Id = "SW-1", Name = "LAN", SwitchType = "External", AllowManagementOS = true },
                        new VirtualSwitch { Id = "SW-2", Name = "ISO", SwitchType = "Private" }],
            HostCards = [Card("LAN", 0), Card("LAN-VLAN100", 100, dhcp: false)],
        };
        var dialogs = new FakeDialogService();
        return (new VirtualSwitchesViewModel(hv, new FakeNatService(), dialogs), hv, dialogs);
    }

    [Fact]
    public async Task Cards_load_when_their_section_opens_and_go_on_external_or_internal_switches()
    {
        var (vm, hv, _) = Page();
        await vm.RefreshAsync();
        Assert.False(vm.HostAdaptersLoaded);                                    // nothing read before the section opens
        Assert.Equal(["LAN"], vm.HostSwitchNames);                              // a private switch has no host card
        await vm.LoadHostAdaptersAsync();
        Assert.Equal(2, vm.HostAdapters.Count);

        vm.NewHostAdapterName = "LAN-VLAN200";
        vm.NewHostAdapterSwitch = "LAN";
        vm.NewHostAdapterVlan = 200;
        vm.NewHostAdapterNoDhcp = true;
        await vm.AddHostAdapterCommand.ExecuteAsync(null);
        Assert.Contains("host-card-add SW-1 LAN-VLAN200 200 False", hv.Calls);   // by switch GUID
        Assert.Equal("", vm.NewHostAdapterName);
    }

    [Fact]
    public async Task A_card_needs_a_valid_name_a_switch_and_a_vlan_in_range()
    {
        var (vm, hv, _) = Page();
        await vm.RefreshAsync();
        vm.NewHostAdapterName = "LAN'1";
        vm.NewHostAdapterSwitch = "LAN";
        await vm.AddHostAdapterCommand.ExecuteAsync(null);
        Assert.Equal(LocalizationService.T("Val_HostAdapterName"), vm.ErrorMessage);
        vm.NewHostAdapterName = "LAN-X";
        vm.NewHostAdapterSwitch = null;
        await vm.AddHostAdapterCommand.ExecuteAsync(null);
        Assert.Equal(LocalizationService.T("HOSTNIC_ErrNoSwitch"), vm.ErrorMessage);
        vm.NewHostAdapterSwitch = "LAN";
        vm.NewHostAdapterVlan = 5000;
        await vm.AddHostAdapterCommand.ExecuteAsync(null);
        Assert.Equal(LocalizationService.T("HOSTNIC_ErrVlan"), vm.ErrorMessage);
        Assert.DoesNotContain(hv.Calls, c => c.StartsWith("host-card-add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_card_changes_vlan_dhcp_and_is_removed_by_its_id()
    {
        var (vm, hv, dialogs) = Page();
        await vm.RefreshAsync();
        await vm.LoadHostAdaptersAsync();
        var card = vm.HostAdapters.Single(c => c.Name == "LAN-VLAN100");

        dialogs.PromptAnswer = "";                                               // empty: untagged
        await vm.ChangeHostAdapterVlanCommand.ExecuteAsync(card);
        dialogs.PromptAnswer = "4095";
        await vm.ChangeHostAdapterVlanCommand.ExecuteAsync(card);
        Assert.Equal(LocalizationService.T("HOSTNIC_ErrVlan"), vm.ErrorMessage);
        await vm.ToggleHostAdapterDhcpCommand.ExecuteAsync(card);               // DHCP was off: turned on
        await vm.RemoveHostAdapterCommand.ExecuteAsync(card);

        Assert.Equal([$"host-card-vlan {card.Id} 0", $"host-card-dhcp {card.Id} True", $"host-card-remove {card.Id}"],
                     hv.Calls.Where(c => c.StartsWith("host-card", StringComparison.Ordinal)));
    }
}
