using HVLab.Models;

namespace HVLab.Services;

/// <summary>Runs Windows PowerShell scripts (see <see cref="PowerShellRunner"/>).</summary>
public interface IPowerShellRunner
{
    Task<string> RunAsync(string script, IProgress<string>? progress = null,
                          TimeSpan? timeout = null, CancellationToken ct = default);
}

/// <summary>Hyper-V host operations: VMs, switches, network adapters, environment.</summary>
public interface IHyperVService
{
    Task<HyperVEnvironment> GetEnvironmentStatusAsync();
    Task StartHyperVServiceAsync();

    Task<List<VirtualMachine>> GetVirtualMachinesAsync();
    Task StartVMAsync(string name);
    Task StopVMAsync(string name);
    Task RemoveVMAsync(string name);
    void ConnectToVMConsole(string name);

    Task<List<VirtualSwitch>> GetVirtualSwitchesAsync();
    Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0);
    Task CreateInternalSwitchAsync(string name, int vlanId = 0);
    Task CreatePrivateSwitchAsync(string name);
    Task RemoveVSwitchAsync(string name);
    Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync();

    Task CreateBlankVmAsync(
        string vmName, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        long diskSizeGB, bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default);

    Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        string? answerFileContent = null, bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>WIM analysis and base VHDX images.</summary>
public interface IVhdxService
{
    Task<List<WimImageInfo>> GetWimImagesAsync(string wimPath);
    Task<List<BaseVhdx>> GetBaseVhdxListAsync(string folder);
    Task CreateBaseVhdxAsync(string vhdxPath, long sizeGB);
    Task DiscardPartialVhdxAsync(string vhdxPath);
    /// <summary>Dismounts the VHDX and waits until Windows has really released it (detach is asynchronous).</summary>
    Task EnsureDetachedAsync(string vhdxPath);
    Task ApplyWindowsImageAsync(
        string vhdxPath, string wimPath, int imageIndex, int generation,
        string? answerFileContent = null,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>NetNat networks.</summary>
public interface INatService
{
    Task<List<NatNetwork>> GetNatNetworksAsync();
    Task CreateNatNetworkAsync(string switchName, string natName, string gatewayIP, int prefixLength);
    Task RemoveNatNetworkAsync(string name);
}

/// <summary>Applies the theme preference ("System" / "Light" / "Dark") to the main window (UI layer).</summary>
public interface IThemeService
{
    void Apply(string theme);
}

/// <summary>Modal dialogs shown by view models (UI layer).</summary>
public interface IDialogService
{
    /// <summary>Asks for confirmation; returns true when the primary button is chosen. Cancel is the default button.</summary>
    Task<bool> ConfirmAsync(string title, string content, string primaryButtonText);
}
