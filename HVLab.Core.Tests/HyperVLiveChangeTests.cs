using System.Security.Principal;
using HVLab.Models;
using HVLab.Services;
using HVLab.Services.Wmi;

namespace HVLab.Core.Tests;

/// <summary>
/// Runs only with HVLAB_LIVE=2 or 3, elevated, on a Hyper-V host (never in CI). These tests CHANGE the host: they
/// create and delete their own HVLT-* VMs, switches and NAT, nothing else, and clean up even when they fail.
/// </summary>
public sealed class LiveChangeFactAttribute : FactAttribute
{
    public LiveChangeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HVLAB_LIVE") is not ("2" or "3"))
            Skip = "Hyper-V live test that changes the host: set HVLAB_LIVE=2 and run elevated";
        else if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "Hyper-V live test: needs elevation";
    }
}

[CollectionDefinition("Hyper-V changes", DisableParallelization = true)]
public sealed class HyperVChangesCollection;

/// <summary>Round trips on real Hyper-V objects: power, settings, checkpoints, delete, NAT, switch type, namesakes.</summary>
[Collection("Hyper-V changes")]
public class HyperVLiveChangeTests
{
    private static readonly HyperVWmi Wmi = new();
    private static readonly PowerShellRunner Ps = new();
    private static readonly HyperVService Hv = new(Ps, Wmi);
    private static readonly NatService Nat = new(Ps, Wmi);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HV-LAB", "livetests");

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..6]}";

    private static async Task<VirtualMachine?> FindAsync(string name)
        => (await Hv.GetVirtualMachinesAsync()).SingleOrDefault(v => v.Name == name);

    /// <summary>Removes the HVLT objects of a test, whatever state the test left them in.</summary>
    private static async Task CleanupAsync(string? vm = null, string? sw = null, string? nat = null)
    {
        if (vm is not null)
            await Ps.RunAsync($"Get-VM | Where-Object Name -eq '{PowerShellRunner.Esc(vm)}' | ForEach-Object {{ " +
                              "Stop-VM -VM $_ -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -VM $_ -Force }; " +
                              $"Remove-Item -LiteralPath (Join-Path '{PowerShellRunner.Esc(Folder)}' '{PowerShellRunner.Esc(vm)}') -Recurse -Force -ErrorAction SilentlyContinue");
        if (nat is not null) await Ps.RunAsync($"Remove-NetNat -Name '{nat}' -Confirm:$false -ErrorAction SilentlyContinue");
        if (sw is not null) await Ps.RunAsync($"Remove-VMSwitch -Name '{sw}' -Force -ErrorAction SilentlyContinue");
    }

    [LiveChangeFact]
    public async Task A_vm_goes_through_power_settings_checkpoints_and_delete()
    {
        var sw = Unique("HVLT-SW"); var name = Unique("HVLT-VM");
        try
        {
            await Hv.CreatePrivateSwitchAsync(sw);
            await Hv.CreateBlankVmAsync(name, sw, 512, 1, 2, Folder, 1, secureBoot: false);
            var vm = await FindAsync(name) ?? throw new Xunit.Sdk.XunitException("VM not created");
            Assert.Equal("Off", vm.State);

            // Settings round trip, by GUID
            var before = await Hv.GetVmSettingsAsync(vm.Id);
            var after = before.Clone();
            after.ProcessorCount = 2;
            await Hv.SetVmSettingsAsync(vm.Id, before, after);
            Assert.Equal(2, (await Hv.GetVmSettingsAsync(vm.Id)).ProcessorCount);

            // Checkpoints: create, rename, apply, remove
            await Hv.CreateCheckpointAsync(vm.Id, "cp1");
            var cp = Assert.Single(await Hv.GetCheckpointsAsync(vm.Id), c => !c.IsNow);
            await Hv.RenameCheckpointAsync(vm.Id, cp.Id, "cp1-renamed");
            await Hv.ApplyCheckpointAsync(vm.Id, cp.Id);
            Assert.Contains(await Hv.GetCheckpointsAsync(vm.Id), c => c.Name == "cp1-renamed");
            await Hv.RemoveCheckpointAsync(vm.Id, cp.Id, false);
            Assert.DoesNotContain(await Hv.GetCheckpointsAsync(vm.Id), c => !c.IsNow);

            // Power through WMI: start, save, start again (restore), turn off
            await Hv.PowerAsync(vm.Id, VmPowerAction.Start);
            Assert.Equal("Running", (await FindAsync(name))!.State);
            await Hv.PowerAsync(vm.Id, VmPowerAction.Save);
            Assert.Equal("Saved", (await FindAsync(name))!.State);
            await Hv.PowerAsync(vm.Id, VmPowerAction.Start);
            await Hv.PowerAsync(vm.Id, VmPowerAction.TurnOff);
            Assert.Equal("Off", (await FindAsync(name))!.State);

            // Delete with its disk
            var plan = await Hv.GetDeletePlanAsync(vm.Id);
            Assert.Contains(plan.Files, f => f.EndsWith($"{name}.vhdx", StringComparison.OrdinalIgnoreCase));
            await Hv.RemoveVMAsync(vm.Id, plan.Files);
            Assert.Null(await FindAsync(name));
            Assert.All(plan.Files, f => Assert.False(File.Exists(f)));
        }
        finally { await CleanupAsync(vm: name, sw: sw); }
    }

    [LiveChangeFact]
    public async Task A_namesake_is_refused_and_the_existing_vm_is_kept()
    {
        var sw = Unique("HVLT-SW"); var name = Unique("HVLT-VM");
        try
        {
            await Hv.CreatePrivateSwitchAsync(sw);
            await Hv.CreateBlankVmAsync(name, sw, 512, 1, 2, Folder, 1, secureBoot: false);
            var first = await FindAsync(name) ?? throw new Xunit.Sdk.XunitException("VM not created");

            var error = await Assert.ThrowsAsync<PowerShellException>(() => Hv.CreateBlankVmAsync(name, sw, 512, 1, 2, Folder, 1));
            Assert.Contains(name, error.Message);
            // The cleanup of the failed creation never touches the VM that existed before
            Assert.Equal(first.Id, (await FindAsync(name))?.Id);
        }
        finally { await CleanupAsync(vm: name, sw: sw); }
    }

    [LiveChangeFact]
    public async Task A_nat_network_outside_a_24_gets_its_masked_prefix()
    {
        var sw = Unique("HVLT-IN"); var nat = Unique("HVLT-NAT");
        try
        {
            await Hv.CreateInternalSwitchAsync(sw);
            await Nat.CreateNatNetworkAsync(sw, nat, "172.31.254.14", 28);
            var created = Assert.Single(await Nat.GetNatNetworksAsync(), n => n.Name == nat);
            Assert.Equal("172.31.254.0/28", created.InternalIPInterfaceAddressPrefix);
            await Nat.RemoveNatNetworkAsync(nat);
            Assert.DoesNotContain(await Nat.GetNatNetworksAsync(), n => n.Name == nat);
        }
        finally { await CleanupAsync(sw: sw, nat: nat); }
    }

    [LiveChangeFact]
    public async Task Switch_namesakes_are_never_touched_by_name()
    {
        var name = Unique("HVLT-DUP");
        try
        {
            await Hv.CreatePrivateSwitchAsync(name);
            var refused = await Assert.ThrowsAsync<PowerShellException>(() => Hv.CreatePrivateSwitchAsync(name));
            Assert.Contains(name, refused.Message);

            // A namesake made outside HV-LAB: Hyper-V allows it
            await Ps.RunAsync($"New-VMSwitch -Name '{name}' -SwitchType Private -ErrorAction Stop | Out-Null");
            var twins = (await Hv.GetVirtualSwitchesAsync()).Where(s => s.Name == name).ToList();
            Assert.Equal(2, twins.Count);
            await Assert.ThrowsAsync<PowerShellException>(() => Hv.RemoveVSwitchAsync(name));
            Assert.Equal(2, (await Hv.GetVirtualSwitchesAsync()).Count(s => s.Name == name));

            // By GUID: that switch and no other
            await Hv.ChangeSwitchTypeAsync(twins[0].Id, "Internal", null);
            Assert.Equal("Internal", (await Hv.GetVirtualSwitchesAsync()).Single(s => s.Id == twins[0].Id).SwitchType);
            await Hv.RemoveVSwitchAsync(twins[0].Id);
            Assert.Equal(twins[1].Id, Assert.Single(await Hv.GetVirtualSwitchesAsync(), s => s.Name == name).Id);
        }
        finally { await CleanupAsync(sw: name); }   // by name on purpose: removes every HVLT namesake left
    }

    [LiveChangeFact]
    public void A_data_folder_prepared_by_someone_else_is_hardened_and_cleaned()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Unique("HVLT-DATA"));
        var target = Directory.CreateTempSubdirectory("hvlt-target-").FullName;
        try
        {
            // As a standard user could leave it: ProgramData's inherited permissions, a settings file, logs as a link
            Directory.CreateDirectory(Path.Combine(folder, "settings"));
            File.WriteAllText(Path.Combine(folder, "settings", "planted.json"), "{}");
            File.WriteAllText(Path.Combine(target, "keep.txt"), "x");
            Directory.CreateSymbolicLink(Path.Combine(folder, "logs"), target);

            AppData.Secure(folder);
            Assert.True(FolderSecurity.IsHardened(folder));
            Assert.False(Directory.Exists(Path.Combine(folder, "settings")));
            Assert.False(Directory.Exists(Path.Combine(folder, "logs")));
            Assert.True(File.Exists(Path.Combine(target, "keep.txt")));          // the link was removed, its target never entered

            // Once hardened, it is trusted: what the app wrote stays
            Directory.CreateDirectory(Path.Combine(folder, "logs"));
            AppData.Secure(folder);
            Assert.True(Directory.Exists(Path.Combine(folder, "logs")));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Delete(target, recursive: true);
        }
    }

    [LiveChangeFact]
    public async Task A_switch_changes_type_and_back()
    {
        var sw = Unique("HVLT-TYPE");
        try
        {
            await Hv.CreatePrivateSwitchAsync(sw);
            await Hv.ChangeSwitchTypeAsync(sw, "Internal", null);
            Assert.Equal("Internal", Assert.Single(await Hv.GetVirtualSwitchesAsync(), s => s.Name == sw).SwitchType);
            await Hv.ChangeSwitchTypeAsync(sw, "Private", null);
            Assert.Equal("Private", Assert.Single(await Hv.GetVirtualSwitchesAsync(), s => s.Name == sw).SwitchType);
        }
        finally { await CleanupAsync(sw: sw); }
    }
}
