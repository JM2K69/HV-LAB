using HVLab.Models;
using HVLab.Services;

namespace HVLab.Core.Tests;

/// <summary>Returns a canned output for every script and records what was run.</summary>
internal sealed class FakePowerShellRunner(Func<string, string> respond) : IPowerShellRunner
{
    public List<string> Scripts { get; } = [];

    public Task<string> RunAsync(string script, IProgress<string>? progress = null,
                                 TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Scripts.Add(script);
        return Task.FromResult(respond(script));
    }
}

internal sealed class FakeDialogService(bool answer = true) : IDialogService
{
    public int Calls { get; private set; }

    public Task<bool> ConfirmAsync(string title, string content, string primaryButtonText)
    {
        Calls++;
        return Task.FromResult(answer);
    }
}

/// <summary>IHyperVService whose VM list can be changed between refreshes.</summary>
internal sealed class FakeHyperVService : IHyperVService
{
    public List<VirtualMachine> Vms { get; set; } = [];
    public Exception? Failure { get; set; }

    public Task<List<VirtualMachine>> GetVirtualMachinesAsync()
        => Failure is not null ? Task.FromException<List<VirtualMachine>>(Failure)
                               : Task.FromResult(Vms.Select(Clone).ToList());

    private static VirtualMachine Clone(VirtualMachine v) => new()
    {
        Name = v.Name, State = v.State, MemoryMB = v.MemoryMB, ProcessorCount = v.ProcessorCount,
        Uptime = v.Uptime, SwitchName = v.SwitchName, VlanInfo = v.VlanInfo, Generation = v.Generation,
    };

    public Task<HyperVEnvironment> GetEnvironmentStatusAsync() => Task.FromResult(HyperVEnvironment.Ok);
    public Task StartHyperVServiceAsync() => Task.CompletedTask;
    public Task StartVMAsync(string name) => Task.CompletedTask;
    public Task StopVMAsync(string name) => Task.CompletedTask;
    public Task RemoveVMAsync(string name) => Task.CompletedTask;
    public void ConnectToVMConsole(string name) { }
    public Task<List<VirtualSwitch>> GetVirtualSwitchesAsync() => Task.FromResult(new List<VirtualSwitch>());
    public Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0) => Task.CompletedTask;
    public Task CreateInternalSwitchAsync(string name, int vlanId = 0) => Task.CompletedTask;
    public Task CreatePrivateSwitchAsync(string name) => Task.CompletedTask;
    public Task RemoveVSwitchAsync(string name) => Task.CompletedTask;
    public Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync() => Task.FromResult(new List<NetworkAdapterInfo>());
    public Task CreateBlankVmAsync(string vmName, string switchName, long memoryMB, int cpuCount, int generation,
        string vmFolder, long diskSizeGB, bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task CreateVMWithDifferencingDiskAsync(string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder, string? answerFileContent = null,
        bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
}
