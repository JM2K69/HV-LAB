using CommunityToolkit.Mvvm.ComponentModel;
using HVLab.Services;

namespace HVLab.Models;

/// <summary>
/// Observable so that in-place diff updates (State, Uptime…) are reflected in the UI
/// without removing and re-adding items to the collection.
/// </summary>
public partial class VirtualMachine : ObservableObject
{
    /// <summary>Hyper-V VM GUID (Msvm_ComputerSystem.Name): the key for WMI calls, stable across renames.</summary>
    public string Id         { get; set; } = string.Empty;
    public string Name       { get; set; } = string.Empty;
    public int    Generation { get; set; } = 2;

    [ObservableProperty] private string state          = string.Empty;
    [ObservableProperty] private int    processorCount;
    [ObservableProperty] private long   memoryMB;
    [ObservableProperty] private string switchName     = string.Empty;
    [ObservableProperty] private string uptime         = string.Empty;
    [ObservableProperty] private string vlanInfo       = string.Empty;  // ex: "10", "10,20", "—"

    // ── Live data (WMI summary, refreshed with the list)
    /// <summary>CPU load in % (running VMs).</summary>
    [ObservableProperty] private int    cpuUsage;
    /// <summary>"OK", "Lost" or empty (no integration services / VM off).</summary>
    [ObservableProperty] private string heartbeat      = string.Empty;
    /// <summary>First IPv4 address: guest KVP, else the host neighbour cache, else the static IP in the notes.</summary>
    [ObservableProperty] private string ipAddress      = string.Empty;
    [ObservableProperty] private string notes          = string.Empty;
    /// <summary>Guest OS name reported by the integration services (KVP), empty when unknown.</summary>
    [ObservableProperty] private string guestOs        = string.Empty;
    /// <summary>Hyper-V VM group (collection) the VM belongs to, empty when none.</summary>
    [ObservableProperty] private string groupName      = string.Empty;

    /// <summary>True while a start/stop/remove action is running on this VM (prevents double clicks).</summary>
    [ObservableProperty] private bool   isBusy;

    // ── Derived display props
    public bool   IsRunning     => State == "Running";
    public bool   IsPaused      => State == "Paused";
    public string MemoryDisplay => $"{MemoryMB} MB";
    public string GenDisplay    => $"Gen {Generation}";
    public string CpuDisplay    => IsRunning ? $"{ProcessorCount} · {CpuUsage} %" : $"{ProcessorCount}";
    public string StateIcon     => IsRunning ? "▶" : "⏹";
    public bool   CanStart      => !IsBusy && State is "Off" or "Saved" or "Paused";
    public bool   CanStop       => !IsBusy && IsRunning;
    public bool   CanTurnOff    => !IsBusy && State is "Running" or "Paused" or "Starting" or "Stopping";
    public bool   CanSave       => !IsBusy && IsRunning;
    public bool   CanPause      => !IsBusy && IsRunning;
    public bool   CanResume     => !IsBusy && IsPaused;
    public bool   CanReset      => !IsBusy && IsRunning;
    public bool   CanConnect    => IsRunning;
    public bool   CanRemove     => !IsBusy && State is "Off" or "Saved";

    /// <summary>Credentials HV-LAB wrote into the notes at creation (account, password, static IP).</summary>
    public VmCredentials Credentials => VmNotes.Read(Notes);

    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(StateIcon));
        OnPropertyChanged(nameof(CpuDisplay));
        NotifyActions();
    }

    partial void OnIsBusyChanged(bool value) => NotifyActions();

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanTurnOff));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanRemove));
    }

    partial void OnMemoryMBChanged(long value) => OnPropertyChanged(nameof(MemoryDisplay));
    partial void OnCpuUsageChanged(int value) => OnPropertyChanged(nameof(CpuDisplay));
    partial void OnProcessorCountChanged(int value) => OnPropertyChanged(nameof(CpuDisplay));
    partial void OnNotesChanged(string value) => OnPropertyChanged(nameof(Credentials));

    /// <summary>Copies the refreshed values of <paramref name="fresh"/> (same VM) into this row.</summary>
    public void UpdateFrom(VirtualMachine fresh)
    {
        Name           = fresh.Name;
        State          = fresh.State;
        ProcessorCount = fresh.ProcessorCount;
        MemoryMB       = fresh.MemoryMB;
        SwitchName     = fresh.SwitchName;
        Uptime         = fresh.Uptime;
        VlanInfo       = fresh.VlanInfo;
        CpuUsage       = fresh.CpuUsage;
        Heartbeat      = fresh.Heartbeat;
        IpAddress      = fresh.IpAddress;
        Notes          = fresh.Notes;
        GuestOs        = fresh.GuestOs;
        GroupName      = fresh.GroupName;
    }
}

/// <summary>Account, password and static IP found in a VM's notes (empty strings when absent).</summary>
public sealed record VmCredentials(string Account, string Password, string IpAddress)
{
    public bool HasPassword => Password.Length > 0;
}
