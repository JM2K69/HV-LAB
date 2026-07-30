using CommunityToolkit.Mvvm.ComponentModel;

namespace HVLab.Models;

/// <summary>
/// Observable so that in-place diff updates (State, Uptime…) are reflected in the UI
/// without removing and re-adding items to the collection.
/// </summary>
public partial class VirtualMachine : ObservableObject
{
    public string Name       { get; set; } = string.Empty;
    public int    Generation { get; set; } = 2;

    [ObservableProperty] private string state          = string.Empty;
    [ObservableProperty] private int    processorCount;
    [ObservableProperty] private long   memoryMB;
    [ObservableProperty] private string switchName     = string.Empty;
    [ObservableProperty] private string uptime         = string.Empty;
    [ObservableProperty] private string vlanInfo       = string.Empty;  // ex: "10", "10,20", "—"

    // ── Derived display props
    public bool   IsRunning     => State == "Running";
    public string MemoryDisplay => $"{MemoryMB} MB";
    public string GenDisplay    => $"Gen {Generation}";
    public string StateIcon     => IsRunning ? "\u25B6" : "\u23F9";

    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StateIcon));
    }

    partial void OnMemoryMBChanged(long value) => OnPropertyChanged(nameof(MemoryDisplay));
}
