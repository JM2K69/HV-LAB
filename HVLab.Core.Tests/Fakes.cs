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
    /// <summary>Check box state returned by <see cref="ConfirmWithOptionAsync"/>; null keeps the default.</summary>
    public bool? Option { get; set; }
    public string? PromptAnswer { get; set; }
    public string LastContent { get; private set; } = "";
    public string? LastOptionLabel { get; private set; }

    public Task<bool> ConfirmAsync(string title, string content, string primaryButtonText)
    {
        Calls++;
        LastContent = content;
        return Task.FromResult(answer);
    }

    public Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string content, string primaryButtonText,
                                                                     string? optionLabel, bool optionDefault)
    {
        Calls++;
        LastContent = content;
        LastOptionLabel = optionLabel;
        return Task.FromResult((answer, optionLabel is not null && (Option ?? optionDefault)));
    }

    public Task<string?> PromptAsync(string title, string label, string initialValue)
    {
        Calls++;
        return Task.FromResult(PromptAnswer);
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
        Id = v.Id, Name = v.Name, State = v.State, MemoryMB = v.MemoryMB, ProcessorCount = v.ProcessorCount,
        Uptime = v.Uptime, SwitchName = v.SwitchName, VlanInfo = v.VlanInfo, Generation = v.Generation,
        GroupName = v.GroupName, Notes = v.Notes, IpAddress = v.IpAddress,
    };

    /// <summary>Calls made by the view models, e.g. "power SRV-01 Start", "remove SRV-01 2 files".</summary>
    public List<string> Calls { get; } = [];
    public VmDeletePlan Plan { get; set; } = new(@"C:\VMs\X", [@"C:\VMs\X\Virtual Hard Disks\X.vhdx"], [@"C:\Bases\BASE_X.vhdx"]);
    public List<CheckpointRow> CheckpointRows { get; set; } = [];
    public VmSettings Settings { get; set; } = new();

    public Task<HyperVEnvironment> GetEnvironmentStatusAsync() => Task.FromResult(HyperVEnvironment.Ok);
    public Task StartHyperVServiceAsync() => Task.CompletedTask;
    public Task PowerAsync(string vmId, VmPowerAction action) { Calls.Add($"power {vmId} {action}"); return Task.CompletedTask; }
    public Task<VmDeletePlan> GetDeletePlanAsync(string name) => Task.FromResult(Plan);
    public Task RemoveVMAsync(string name, IReadOnlyList<string>? files = null)
    {
        Calls.Add($"remove {name} {files?.Count ?? 0} files");
        Vms.RemoveAll(v => v.Name == name);
        return Task.CompletedTask;
    }
    public void ConnectToVMConsole(string name) { }
    public Task<VmThumbnail?> GetThumbnailAsync(string vmId, int maxWidth, int maxHeight) => Task.FromResult<VmThumbnail?>(null);
    public Task TypeTextAsync(string vmId, string text, string language, bool pressEnter)
    { Calls.Add($"type {vmId} {text} {language} {pressEnter}"); return Task.CompletedTask; }
    public Task SendCtrlAltDelAsync(string vmId) { Calls.Add($"cad {vmId}"); return Task.CompletedTask; }
    public Task SendScancodesAsync(string vmId, byte[] scancodes)
    { Calls.Add($"keys {vmId} {Convert.ToHexString(scancodes)}"); return Task.CompletedTask; }
    /// <summary>When set, mouse moves wait for it (to test that moves are coalesced).</summary>
    public TaskCompletionSource? MoveGate { get; set; }
    public Task MouseMoveAsync(string vmId, int x, int y) { Calls.Add($"move {vmId} {x},{y}"); return MoveGate?.Task ?? Task.CompletedTask; }
    public Task MouseButtonAsync(string vmId, int button, bool down) { Calls.Add($"button {vmId} {button} {down}"); return Task.CompletedTask; }
    public Task MouseWheelAsync(string vmId, int delta) { Calls.Add($"wheel {vmId} {delta}"); return Task.CompletedTask; }
    public bool EnhancedSessionHost { get; set; } = true;
    public Task<bool> GetEnhancedSessionHostAsync() => Task.FromResult(EnhancedSessionHost);
    public Task SetEnhancedSessionHostAsync(bool enabled)
    { Calls.Add($"enhanced {enabled}"); EnhancedSessionHost = enabled; return Task.CompletedTask; }
    public Task<List<CheckpointRow>> GetCheckpointsAsync(string vmId) => Task.FromResult(CheckpointRows.ToList());
    public Task CreateCheckpointAsync(string vmName, string checkpointName) { Calls.Add($"checkpoint {vmName} {checkpointName}"); return Task.CompletedTask; }
    public Task ApplyCheckpointAsync(string vmName, string checkpointId) { Calls.Add($"apply {vmName} {checkpointId}"); return Task.CompletedTask; }
    public Task RemoveCheckpointAsync(string vmName, string checkpointId, bool withChildren)
    { Calls.Add($"remove-checkpoint {vmName} {checkpointId} {withChildren}"); return Task.CompletedTask; }
    public Task RenameCheckpointAsync(string vmName, string checkpointId, string newName)
    { Calls.Add($"rename-checkpoint {vmName} {checkpointId} {newName}"); return Task.CompletedTask; }
    public Task<VmSettings> GetVmSettingsAsync(string vmName) => Task.FromResult(Settings.Clone());
    public Task SetVmSettingsAsync(string vmName, VmSettings before, VmSettings after)
    { Calls.Add($"settings {vmName} {string.Join(" | ", HyperVService.SettingsScript(before, after))}"); return Task.CompletedTask; }
    public Task AddToGroupAsync(string groupName, IReadOnlyList<string> vmNames)
    {
        Calls.Add($"group {groupName} {string.Join(",", vmNames)}");
        foreach (var v in Vms.Where(v => vmNames.Contains(v.Name))) v.GroupName = groupName;
        return Task.CompletedTask;
    }
    public Task RemoveFromGroupsAsync(IReadOnlyList<string> vmNames) { Calls.Add($"ungroup {string.Join(",", vmNames)}"); return Task.CompletedTask; }

    public List<VirtualSwitch> Switches { get; set; } = [];
    public List<VmNetworkAdapter> VmAdapters { get; set; } = [];
    public Dictionary<string, string> HostAddresses { get; set; } = [];
    public List<NetworkAdapterInfo> PhysicalAdapters { get; set; } = [];
    public IcsStatus? Ics { get; set; }
    public Task<List<VmNetworkAdapter>> GetVmNetworkAdaptersAsync() => Task.FromResult(VmAdapters.ToList());
    public Task<Dictionary<string, string>> GetHostAddressesAsync() => Task.FromResult(new Dictionary<string, string>(HostAddresses));
    public Task ChangeSwitchTypeAsync(string name, string newType, string? netAdapter)
    { Calls.Add($"switch-type {name} {newType} {netAdapter}"); return Task.CompletedTask; }
    public Task<IcsStatus?> GetIcsStatusAsync() => Task.FromResult(Ics);
    public Task EnableIcsAsync(string publicAdapter, string switchName)
    { Calls.Add($"ics-on {publicAdapter} {switchName}"); Ics = new IcsStatus(publicAdapter, $"vEthernet ({switchName})"); return Task.CompletedTask; }
    public Task DisableIcsAsync() { Calls.Add("ics-off"); Ics = null; return Task.CompletedTask; }
    public Task<List<VirtualSwitch>> GetVirtualSwitchesAsync() => Task.FromResult(Switches.ToList());
    public Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0) => Task.CompletedTask;
    public Task CreateInternalSwitchAsync(string name, int vlanId = 0) => Task.CompletedTask;
    public Task CreatePrivateSwitchAsync(string name) => Task.CompletedTask;
    public Task RemoveVSwitchAsync(string name) => Task.CompletedTask;
    public Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync() => Task.FromResult(PhysicalAdapters.ToList());
    /// <summary>Notes passed to each created VM, by VM name.</summary>
    public Dictionary<string, string?> CreatedNotes { get; } = [];
    /// <summary>Secure Boot template and cloud-init seed of each differencing VM, by VM name.</summary>
    public Dictionary<string, (string Template, CloudInitSeed? Seed)> CreatedFirmware { get; } = [];

    public Task CreateBlankVmAsync(string vmName, string switchName, long memoryMB, int cpuCount, int generation,
        string vmFolder, long diskSizeGB, bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        CreatedNotes[vmName] = notes;
        return Task.CompletedTask;
    }

    public Task CreateVMWithDifferencingDiskAsync(string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder, string? answerFileContent = null,
        bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        string secureBootTemplate = BaseImageInfo.WindowsSecureBootTemplate, CloudInitSeed? cloudInit = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        CreatedNotes[vmName] = notes;
        CreatedFirmware[vmName] = (secureBootTemplate, cloudInit);
        return Task.CompletedTask;
    }
}
