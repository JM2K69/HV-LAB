using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class HyperVParsingTests
{
    private const string TwoVms = """
        [{"Name":"SRV-01","State":"Running","ProcessorCount":2,"MemoryMB":4096,"Generation":2,"SwitchName":"LAN","Uptime":"01:02:03","VlanInfo":"10"},
         {"Name":"SRV-02","State":"Off","ProcessorCount":1,"MemoryMB":2048,"Generation":1,"SwitchName":null,"Uptime":"00:00:00","VlanInfo":12}]
        """;

    [Fact]
    public async Task Vms_are_parsed_from_json()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => TwoVms));
        var vms = await hv.GetVirtualMachinesAsync();

        Assert.Equal(2, vms.Count);
        Assert.True(vms[0].IsRunning);
        Assert.Equal(4096, vms[0].MemoryMB);
        Assert.Equal("", vms[1].SwitchName);     // null → empty
        Assert.Equal("12", vms[1].VlanInfo);      // number → text (no exception)
    }

    [Fact]
    public async Task Stray_output_before_the_json_is_ignored()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "WARNING: something\r\n" + TwoVms));
        Assert.Equal(2, (await hv.GetVirtualMachinesAsync()).Count);
    }

    [Fact]
    public async Task Unreadable_output_is_an_error_not_an_empty_list()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "[not json"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(hv.GetVirtualMachinesAsync);
        Assert.Contains("[not json", ex.Message);
    }

    [Fact]
    public async Task Empty_array_gives_no_vm()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "[]"));
        Assert.Empty(await hv.GetVirtualMachinesAsync());
    }

    [Fact]
    public async Task Vm_names_are_escaped_in_scripts()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).StartVMAsync("it's");
        Assert.Contains("'it''s'", runner.Scripts.Single());
    }

    [Fact]
    public async Task Invalid_vm_name_never_reaches_powershell()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new HyperVService(runner).CreateBlankVmAsync(@"..\x", "LAN", 2048, 2, 2, @"C:\VMs", 40));
        Assert.Empty(runner.Scripts);
    }

    [Fact]
    public async Task Answer_file_is_embedded_not_written_to_temp()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).CreateVMWithDifferencingDiskAsync(
            "SRV-01", @"C:\Base\BASE.vhdx", "LAN", 2048, 2, 2, @"C:\VMs", answerFileContent: "<unattend/>");
        var script = runner.Scripts.Single();
        Assert.Contains("FromBase64String", script);
        Assert.DoesNotContain("<unattend/>", script);
        Assert.DoesNotContain("hvlab_unattend_", script);
    }
}

public class VirtualMachinesViewModelTests
{
    private static VirtualMachine Vm(string name, string state) => new() { Name = name, State = state };

    [Fact]
    public async Task Refresh_updates_rows_in_place()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off"), Vm("B", "Running")] };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        var rowA = vm.VirtualMachines.Single(v => v.Name == "A");

        hv.Vms = [Vm("A", "Running"), Vm("C", "Off")];
        await vm.RefreshAsync();

        Assert.Same(rowA, vm.VirtualMachines.Single(v => v.Name == "A")); // same instance, updated
        Assert.Equal("Running", rowA.State);
        Assert.Equal(["A", "C"], vm.VirtualMachines.Select(v => v.Name).Order());
    }

    [Fact]
    public async Task Empty_state_only_after_a_successful_refresh()
    {
        var hv = new FakeHyperVService();
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        Assert.False(vm.IsEmpty);
        await vm.RefreshAsync();
        Assert.True(vm.IsEmpty);

        hv.Failure = new InvalidOperationException("boom");
        await vm.RefreshAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.IsEmpty);
        Assert.Equal("boom", vm.ErrorMessage);
    }

    [Fact]
    public async Task Remove_asks_for_confirmation_and_stops_when_declined()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off")] };
        var dialogs = new FakeDialogService(answer: false);
        var vm = new VirtualMachinesViewModel(hv, dialogs);
        await vm.RefreshAsync();

        await vm.RemoveVmCommand.ExecuteAsync(vm.VirtualMachines[0]);
        Assert.Equal(1, dialogs.Calls);
        Assert.False(vm.VirtualMachines[0].IsBusy);
    }

    [Fact]
    public void Action_availability_follows_state_and_busy_flag()
    {
        var v = Vm("A", "Off");
        Assert.True(v.CanStart);  Assert.False(v.CanStop); Assert.True(v.CanRemove);
        v.State = "Running";
        Assert.False(v.CanStart); Assert.True(v.CanStop);  Assert.False(v.CanRemove);
        v.IsBusy = true;
        Assert.False(v.CanStop);
    }
}
