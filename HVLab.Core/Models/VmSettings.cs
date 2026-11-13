using CommunityToolkit.Mvvm.ComponentModel;

namespace HVLab.Models;

/// <summary>
/// Settings that can be changed after creation (the "Settings" dialog of the VM list).
/// Processor and startup memory need the VM off; the ISO, the switch and the VLAN can change while it runs.
/// </summary>
public partial class VmSettings : ObservableObject
{
    [ObservableProperty] private int    processorCount = 2;
    /// <summary>Nested virtualization (Hyper-V, WSL or containers inside the VM): needs static memory and MAC spoofing.</summary>
    [ObservableProperty] private bool   nested;
    [ObservableProperty] private int    startupMB = 2048;
    [ObservableProperty] private bool   dynamicMemory;
    [ObservableProperty] private int    minimumMB = 512;
    [ObservableProperty] private int    maximumMB = 4096;
    [ObservableProperty] private string isoPath = "";
    [ObservableProperty] private string switchName = "";
    [ObservableProperty] private int    vlanId;
    /// <summary>State when the settings were read: a running VM keeps its processor and startup memory.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEditOffline))] private bool isRunning;

    public bool CanEditOffline => !IsRunning;

    partial void OnNestedChanged(bool value)
    {
        if (value) DynamicMemory = false;   // nested virtualization refuses dynamic memory
    }

    public VmSettings Clone() => (VmSettings)MemberwiseClone();

    /// <summary>Localization key of the first invalid value, or null.</summary>
    public string? Validate()
    {
        if (ProcessorCount is < 1 or > 64) return "Val_VmCpu";
        if (StartupMB < 32 || StartupMB % 2 != 0) return "Val_VmMemory";
        if (DynamicMemory && (MinimumMB < 32 || MinimumMB % 2 != 0 || MaximumMB % 2 != 0
                              || MinimumMB > StartupMB || StartupMB > MaximumMB))
            return "Val_VmMemoryRange";
        if (Nested && DynamicMemory) return "Val_VmNestedDynamic";
        if (VlanId is < 0 or > 4094) return "Val_VmVlan";
        if (IsoPath.Length > 0 && !IsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return "Val_VmIso";
        return null;
    }
}
