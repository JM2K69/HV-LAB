using System.Security.Principal;
using System.Text.Json;
using HVLab.Services;
using HVLab.Services.Wmi;

namespace HVLab.Core.Tests;

/// <summary>Runs only on a Hyper-V host, elevated, with HVLAB_LIVE=1 (never in CI).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HVLAB_LIVE") != "1")
            Skip = "Hyper-V live test: set HVLAB_LIVE=1 and run elevated";
        else if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "Hyper-V live test: needs elevation";
    }
}

/// <summary>
/// Read-only comparison of the WMI engine with the Hyper-V cmdlets on the real host: same VMs, states,
/// generations, memory, switches, NAT networks and checkpoints. Nothing is changed on the host.
/// </summary>
public class HyperVLiveTests
{
    private static readonly HyperVWmi Wmi = new();
    private static readonly PowerShellRunner Ps = new();

    private static async Task<JsonElement[]> PsJson(string script)
    {
        var output = await Ps.RunAsync($"Import-Module Hyper-V; @({script}) | ConvertTo-Json -Depth 3 -Compress");
        var json = HyperVService.ExtractJson(output);
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json.StartsWith('{') ? $"[{json}]" : json);
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [LiveFact]
    public async Task Vm_list_matches_get_vm()
    {
        var wmi = Wmi.GetVirtualMachines();
        var ps = await PsJson("Get-VM | ForEach-Object { [PSCustomObject]@{ Id = \"$($_.Id)\".ToUpper(); Name = $_.Name; State = \"$($_.State)\"; " +
                              "Generation = $_.Generation; Cpu = $_.ProcessorCount; StartupMB = [long]($_.MemoryStartup / 1MB); " +
                              "Switch = \"$(($_.NetworkAdapters | Select-Object -First 1).SwitchName)\" } }");
        Assert.Equal(ps.Length, wmi.Count);
        foreach (var p in ps)
        {
            var w = Assert.Single(wmi, v => v.Id == p.GetProperty("Id").GetString());
            Assert.Equal(p.GetProperty("Name").GetString(), w.Name);
            Assert.Equal(p.GetProperty("State").GetString(), w.State);
            Assert.Equal(p.GetProperty("Generation").GetInt32(), w.Generation);
            Assert.Equal(p.GetProperty("Cpu").GetInt32(), w.ProcessorCount);
            Assert.Equal(p.GetProperty("Switch").GetString(), w.SwitchName);
            if (w.State == "Off") Assert.Equal(p.GetProperty("StartupMB").GetInt64(), w.MemoryMB);
        }
    }

    [LiveFact]
    public async Task Switches_match_get_vmswitch()
    {
        var wmi = Wmi.GetSwitches();
        var ps = await PsJson("Get-VMSwitch | ForEach-Object { [PSCustomObject]@{ Name = $_.Name; Type = \"$($_.SwitchType)\"; Mgmt = [bool]$_.AllowManagementOS } }");
        Assert.Equal(ps.Length, wmi.Count);
        foreach (var p in ps)
        {
            var w = Assert.Single(wmi, s => s.Name == p.GetProperty("Name").GetString());
            Assert.Equal(p.GetProperty("Type").GetString(), w.SwitchType);
            Assert.Equal(p.GetProperty("Mgmt").GetBoolean(), w.AllowManagementOS);
        }
    }

    [LiveFact]
    public async Task Nat_networks_match_get_netnat()
    {
        var wmi = Wmi.GetNatNetworks();
        var ps = await PsJson("Get-NetNat | ForEach-Object { [PSCustomObject]@{ Name = $_.Name; Prefix = $_.InternalIPInterfaceAddressPrefix } }");
        Assert.Equal(ps.Select(p => p.GetProperty("Name").GetString()).Order(), wmi.Select(n => n.Name).Order());
        foreach (var p in ps)
            Assert.Equal(p.GetProperty("Prefix").GetString(), wmi.Single(n => n.Name == p.GetProperty("Name").GetString()).InternalIPInterfaceAddressPrefix);
    }

    [LiveFact]
    public async Task Checkpoints_match_get_vmsnapshot()
    {
        var ps = await PsJson("Get-VMSnapshot -VMName * | ForEach-Object { [PSCustomObject]@{ VmId = \"$($_.VMId)\".ToUpper(); Id = \"$($_.Id)\".ToUpper(); Name = $_.Name } }");
        foreach (var vm in ps.GroupBy(p => p.GetProperty("VmId").GetString()!))
        {
            var rows = Wmi.GetCheckpoints(vm.Key).Where(r => !r.IsNow).ToList();
            Assert.Equal(vm.Select(p => p.GetProperty("Id").GetString()).Order(), rows.Select(r => r.Id).Order());
            Assert.Equal(vm.Select(p => p.GetProperty("Name").GetString()).Order(), rows.Select(r => r.Name).Order());
        }
    }

    [LiveFact]
    public void Environment_is_ok() => Assert.Equal(HyperVEnvironment.Ok, Wmi.GetEnvironment());

    [LiveFact]
    public async Task Vm_adapters_match_get_vmnetworkadapter()
    {
        var wmi = Wmi.GetVmNetworkAdapters();
        var ps = await PsJson("Get-VMNetworkAdapter -VMName * | ForEach-Object { [PSCustomObject]@{ VmId = \"$($_.VMId)\".ToUpper(); " +
                              "Switch = \"$($_.SwitchName)\"; Vlan = \"$((Get-VMNetworkAdapterVlan -VMNetworkAdapter $_).OperationMode)\" } }");
        Assert.Equal(ps.Length, wmi.Count);
        foreach (var vm in ps.GroupBy(p => p.GetProperty("VmId").GetString()!))
        {
            Assert.Equal(vm.Select(p => p.GetProperty("Switch").GetString() ?? "").Order(),
                         wmi.Where(a => a.VmId == vm.Key).Select(a => a.SwitchName).Order());
            // A tagged adapter (Access or Trunk) must show a VLAN in WMI too
            Assert.Equal(vm.Count(p => p.GetProperty("Vlan").GetString() is "Access" or "Trunk"),
                         wmi.Count(a => a.VmId == vm.Key && a.Vlan.Length > 0));
        }
    }

    [LiveFact]
    public async Task Physical_adapters_match_get_netadapter()
    {
        var ps = await PsJson("Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.HardwareInterface } | ForEach-Object { [PSCustomObject]@{ Name = $_.Name; Mac = $_.MacAddress } }");
        var wmi = Wmi.GetPhysicalAdapters();
        Assert.Equal(ps.Select(p => p.GetProperty("Name").GetString()).Order(), wmi.Select(a => a.Name).Order());
        foreach (var p in ps)
            Assert.Equal(p.GetProperty("Mac").GetString(), wmi.Single(a => a.Name == p.GetProperty("Name").GetString()).MacAddress);
    }

    [LiveFact]
    public async Task Host_addresses_match_get_netipaddress()
    {
        var ps = await PsJson("Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '169.254.*' } | ForEach-Object { [PSCustomObject]@{ Alias = $_.InterfaceAlias; Ip = $_.IPAddress } }");
        var wmi = Wmi.GetHostAddresses();
        foreach (var alias in ps.GroupBy(p => p.GetProperty("Alias").GetString()!))
            Assert.Contains(wmi[alias.Key], alias.Select(p => p.GetProperty("Ip").GetString()));
    }
}
