using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HVLab.Models;

/// <summary>
/// Settings that can be changed after creation (the "Settings" dialog of the VM list).
/// Processor and startup memory need the VM off; the ISO and the network settings of the first adapter
/// (switch, VLAN, port guards) can change while it runs.
/// </summary>
public partial class VmSettings : ObservableObject
{
    public const string VlanUntagged = "Untagged", VlanAccess = "Access", VlanTrunk = "Trunk";

    [ObservableProperty] private int    processorCount = 2;
    /// <summary>Nested virtualization (Hyper-V, WSL or containers inside the VM): needs static memory and MAC spoofing.</summary>
    [ObservableProperty] private bool   nested;
    [ObservableProperty] private int    startupMB = 2048;
    [ObservableProperty] private bool   dynamicMemory;
    [ObservableProperty] private int    minimumMB = 512;
    [ObservableProperty] private int    maximumMB = 4096;
    [ObservableProperty] private string isoPath = "";
    [ObservableProperty] private string switchName = "";

    // VLAN of the first adapter: untagged, access (one VLAN) or trunk (native VLAN + allowed list)
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAccess), nameof(IsTrunk))] private string vlanMode = VlanUntagged;
    [ObservableProperty] private int    vlanId;
    [ObservableProperty] private int    nativeVlanId;
    /// <summary>Trunk VLANs, e.g. "10,20,100-110".</summary>
    [ObservableProperty] private string allowedVlans = "";

    // Port guards of the first adapter
    /// <summary>Drops DHCP offers sent by the VM (a lab DHCP server must not leak).</summary>
    [ObservableProperty] private bool   dhcpGuard;
    /// <summary>Drops router advertisements and redirects sent by the VM.</summary>
    [ObservableProperty] private bool   routerGuard;
    /// <summary>Lets the VM send frames with other MAC addresses (nested VMs, NLB, containers).</summary>
    [ObservableProperty] private bool   macSpoofing;

    /// <summary>State when the settings were read: a running VM keeps its processor and startup memory.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEditOffline))] private bool isRunning;

    public bool CanEditOffline => !IsRunning;
    public bool IsAccess => VlanMode == VlanAccess;
    public bool IsTrunk  => VlanMode == VlanTrunk;
    public static IReadOnlyList<string> VlanModes { get; } = [VlanUntagged, VlanAccess, VlanTrunk];

    partial void OnNestedChanged(bool value)
    {
        if (!value) return;
        DynamicMemory = false;   // nested virtualization refuses dynamic memory
        MacSpoofing   = true;    // the inner VMs reach the network through this adapter
    }

    public VmSettings Clone() => (VmSettings)MemberwiseClone();

    [GeneratedRegex(@"^\d{1,4}(-\d{1,4})?(,\d{1,4}(-\d{1,4})?)*$")]
    private static partial Regex VlanListRegex();

    /// <summary>"10,20-30" with every VLAN in 1–4094 and ranges in order.</summary>
    public static bool IsValidVlanList(string list)
    {
        var compact = list.Replace(" ", "");
        if (!VlanListRegex().IsMatch(compact)) return false;
        foreach (var part in compact.Split(','))
        {
            var bounds = part.Split('-').Select(int.Parse).ToArray();
            if (bounds.Any(v => v is < 1 or > 4094) || (bounds.Length == 2 && bounds[0] > bounds[1])) return false;
        }
        return true;
    }

    /// <summary>Localization key of the first invalid value, or null.</summary>
    public string? Validate()
    {
        if (ProcessorCount is < 1 or > 64) return "Val_VmCpu";
        if (StartupMB < 32 || StartupMB % 2 != 0) return "Val_VmMemory";
        if (DynamicMemory && (MinimumMB < 32 || MinimumMB % 2 != 0 || MaximumMB % 2 != 0
                              || MinimumMB > StartupMB || StartupMB > MaximumMB))
            return "Val_VmMemoryRange";
        if (Nested && DynamicMemory) return "Val_VmNestedDynamic";
        if (IsAccess && VlanId is < 1 or > 4094) return "Val_VmVlan";
        if (IsTrunk && (NativeVlanId is < 0 or > 4094 || !IsValidVlanList(AllowedVlans))) return "Val_VmTrunk";
        if (IsoPath.Length > 0 && !IsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return "Val_VmIso";
        return null;
    }
}
