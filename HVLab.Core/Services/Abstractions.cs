using HVLab.Models;

namespace HVLab.Services;

/// <summary>Runs Windows PowerShell scripts (see <see cref="PowerShellRunner"/>).</summary>
public interface IPowerShellRunner
{
    Task<string> RunAsync(string script, IProgress<string>? progress = null,
                          TimeSpan? timeout = null, CancellationToken ct = default);
}

/// <summary>
/// Direct WMI access for the frequent Hyper-V operations (see <see cref="Wmi.HyperVWmi"/>).
/// Synchronous: <see cref="HyperVService"/> calls it from a worker thread.
/// </summary>
public interface IHyperVWmi
{
    HyperVEnvironment GetEnvironment();
    List<VirtualMachine> GetVirtualMachines();
    void ChangeState(string vmId, VmPowerAction action);
    VmThumbnail? GetThumbnail(string vmId, int maxWidth, int maxHeight);
    void TypeScancodes(string vmId, byte[] scancodes);
    void TypeCtrlAltDel(string vmId);
    List<CheckpointRow> GetCheckpoints(string vmId);
    List<VirtualSwitch> GetSwitches();
    List<NatNetwork> GetNatNetworks();
    List<VmNetworkAdapter> GetVmNetworkAdapters();
    Dictionary<string, string> GetHostAddresses();
    List<NetworkAdapterInfo> GetPhysicalAdapters();
    bool GetEnhancedSessionHost();
    /// <summary>Host memory and processors, and the free space of the drives holding <paramref name="folders"/>.</summary>
    HostCapacity GetHostCapacity(IEnumerable<string> folders);
}

/// <summary>The Hyper-V host itself: environment check, capacity, enhanced session mode.</summary>
public interface IHostService
{
    Task<HyperVEnvironment> GetEnvironmentStatusAsync();
    Task StartHyperVServiceAsync();
    Task<HostCapacity> GetHostCapacityAsync();

    // Enhanced session mode of the host (vmconnect with clipboard, drives, dynamic resolution)
    Task<bool> GetEnhancedSessionHostAsync();
    Task SetEnhancedSessionHostAsync(bool enabled);
}

/// <summary>Virtual machines: list, power, delete, checkpoints, settings, notes, groups and creation.</summary>
public interface IVmService
{
    Task<List<VirtualMachine>> GetVirtualMachinesAsync();
    Task PowerAsync(string vmId, VmPowerAction action);
    /// <summary>Disks that a delete with files would remove (and the shared parents it keeps).</summary>
    Task<VmDeletePlan> GetDeletePlanAsync(string name);
    /// <param name="files">Files of <see cref="GetDeletePlanAsync"/> to delete after the VM, or null to keep every file.</param>
    Task RemoveVMAsync(string name, IReadOnlyList<string>? files = null);
    void ConnectToVMConsole(string name);

    // Checkpoints (list through WMI, changes through PowerShell)
    Task<List<CheckpointRow>> GetCheckpointsAsync(string vmId);
    Task CreateCheckpointAsync(string vmName, string checkpointName);
    Task ApplyCheckpointAsync(string vmName, string checkpointId);
    Task RemoveCheckpointAsync(string vmName, string checkpointId, bool withChildren);
    Task RenameCheckpointAsync(string vmName, string checkpointId, string newName);

    // Settings after creation
    Task<VmSettings> GetVmSettingsAsync(string vmName);
    Task SetVmSettingsAsync(string vmName, VmSettings before, VmSettings after);
    /// <summary>Replaces the Hyper-V notes of the VM (credentials and tags lines, see <see cref="VmNotes"/>).</summary>
    Task SetNotesAsync(string vmId, string notes);

    // Hyper-V VM groups
    Task AddToGroupAsync(string groupName, IReadOnlyList<string> vmNames);
    Task RemoveFromGroupsAsync(IReadOnlyList<string> vmNames);

    /// <param name="notes">Hyper-V notes of the VM (see <see cref="VmNotes"/>).</param>
    Task CreateBlankVmAsync(
        string vmName, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        long diskSizeGB, bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <param name="notes">Hyper-V notes of the VM (see <see cref="VmNotes"/>).</param>
    /// <param name="secureBootTemplate">Generation 2 only: MicrosoftWindows, or MicrosoftUEFICertificateAuthority for Linux.</param>
    /// <param name="cloudInit">Linux clone: NoCloud seed written to a second disk labelled CIDATA.</param>
    Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        string? answerFileContent = null, bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        string secureBootTemplate = BaseImageInfo.WindowsSecureBootTemplate, CloudInitSeed? cloudInit = null,
        IProgress<string>? progress = null, CancellationToken ct = default, string? staticMac = null);
}

/// <summary>
/// Inside a running VM: a preview of its screen, the password or Ctrl+Alt+Del typed on its keyboard (WMI), and the
/// guest automation of Windows guests. Working in the VM itself is left to vmconnect.
/// </summary>
public interface IGuestService
{
    Task<VmThumbnail?> GetThumbnailAsync(string vmId, int maxWidth, int maxHeight);
    /// <summary>Types <paramref name="text"/> into the VM as the keys of the guest layout <paramref name="language"/> (fr-FR…).</summary>
    Task TypeTextAsync(string vmId, string text, string language, bool pressEnter);
    Task SendCtrlAltDelAsync(string vmId);

    // Guest automation (Windows guests): PowerShell Direct over VMBus, no network needed
    /// <summary>Runs <paramref name="script"/> in the guest as <paramref name="account"/>; returns its output as text.</summary>
    Task<string> InvokeInGuestAsync(string vmId, string account, string password, string script);
    /// <summary>Copies a host file into <paramref name="guestFolder"/> (Guest Service Interface), creating the folder.</summary>
    Task CopyToGuestAsync(string vmId, string sourcePath, string guestFolder);
}

/// <summary>Virtual switches, network adapters, topology data and connection sharing.</summary>
public interface INetworkService
{
    Task<List<VirtualSwitch>> GetVirtualSwitchesAsync();
    // A name already used by a switch is refused (Hyper-V would accept the namesake)
    Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0);
    Task CreateInternalSwitchAsync(string name, int vlanId = 0);
    Task CreatePrivateSwitchAsync(string name);
    /// <param name="switchIdOrName">The switch GUID (<see cref="Models.VirtualSwitch.Target"/>), else a name that must be unique.</param>
    Task RemoveVSwitchAsync(string switchIdOrName);
    /// <summary>
    /// Changes the type of a switch in place (External needs <paramref name="netAdapter"/>). On failure the previous
    /// type and the host adapter's static IPv4 addresses are put back before the error is reported.
    /// </summary>
    /// <param name="switchIdOrName">The switch GUID (<see cref="Models.VirtualSwitch.Target"/>), else a name that must be unique.</param>
    Task ChangeSwitchTypeAsync(string switchIdOrName, string newType, string? netAdapter);

    // Topology data (WMI)
    Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync();
    Task<List<VmNetworkAdapter>> GetVmNetworkAdaptersAsync();
    Task<Dictionary<string, string>> GetHostAddressesAsync();

    /// <summary>Current Internet connection sharing: public and private connection names, null when off.</summary>
    Task<IcsStatus?> GetIcsStatusAsync();
    /// <summary>
    /// Shares <paramref name="publicAdapter"/> with the internal switch <paramref name="switchName"/>: Windows then gives
    /// its host adapter 192.168.137.1/24 and hands out DHCP addresses to the VMs. Any other sharing is turned off first.
    /// </summary>
    Task EnableIcsAsync(string publicAdapter, string switchName);
    Task DisableIcsAsync();
}

/// <summary>
/// Every Hyper-V operation (one <see cref="HyperVService"/> implements all four parts). Code that needs one part only
/// takes that part: the host, the VMs, a guest, or the network.
/// </summary>
public interface IHyperVService : IHostService, IVmService, IGuestService, INetworkService;

/// <summary>WIM analysis and base VHDX images.</summary>
public interface IVhdxService
{
    Task<List<WimImageInfo>> GetWimImagesAsync(string wimPath);
    Task<List<BaseVhdx>> GetBaseVhdxListAsync(string folder);
    Task CreateBaseVhdxAsync(string vhdxPath, long sizeGB);
    /// <summary>Parent VHDX (lower-case path) → names of the VMs whose disk chains use it.</summary>
    Task<Dictionary<string, List<string>>> GetBaseDependentsAsync();
    Task DiscardPartialVhdxAsync(string vhdxPath);
    /// <summary>Dismounts the VHDX and waits until Windows has really released it (detach is asynchronous).</summary>
    Task EnsureDetachedAsync(string vhdxPath);
    Task ApplyWindowsImageAsync(
        string vhdxPath, string wimPath, int imageIndex, int generation,
        string? answerFileContent = null,
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Copies the system disk of a stopped VM (no checkpoint, one disk besides a cloud-init seed) into
    /// <paramref name="destPath"/> as a standalone dynamic VHDX; the VM is left untouched.
    /// Returns its firmware settings, which the clones reuse.
    /// </summary>
    Task<VmDiskCopy> CopyVmDiskAsync(string vmName, string destPath,
        IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>Result of <see cref="IVhdxService.CopyVmDiskAsync"/>.</summary>
public sealed record VmDiskCopy(int Generation, bool SecureBoot, string SecureBootTemplate, long SizeGB);

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

    /// <summary>
    /// Confirmation with a check box (e.g. "also delete the disks"); <paramref name="optionLabel"/> null hides it.
    /// Returns whether the primary button was chosen and the state of the check box.
    /// </summary>
    Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string content, string primaryButtonText,
                                                              string? optionLabel, bool optionDefault);

    /// <summary>Asks for one line of text; null when cancelled or (unless <paramref name="allowEmpty"/>) left empty.</summary>
    /// <param name="allowEmpty">An empty answer is returned as "" (e.g. removing every tag), not as a cancel.</param>
    Task<string?> PromptAsync(string title, string label, string initialValue, bool allowEmpty = false);

    /// <summary>Asks for several lines (a script); null when cancelled or left empty.</summary>
    Task<string?> PromptTextAsync(string title, string label, string initialValue);

    /// <summary>Shows a read-only text that can be selected and copied (a command's output).</summary>
    Task ShowTextAsync(string title, string text);
}
