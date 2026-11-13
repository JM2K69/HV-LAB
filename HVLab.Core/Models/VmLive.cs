namespace HVLab.Models;

/// <summary>Power actions of the VM list (WMI RequestStateChange or the guest shutdown component).</summary>
public enum VmPowerAction { Start, Shutdown, TurnOff, Save, Pause, Resume, Reset, Restart }

/// <summary>One checkpoint of a VM (Msvm_VirtualSystemSettingData of type Snapshot:Realized).</summary>
public sealed record VmCheckpoint(string Id, string Name, string? ParentId, DateTime? Created);

/// <summary>Row of the flattened checkpoint tree; <see cref="IsNow"/> marks the VM's current position.</summary>
public sealed record CheckpointRow(string Id, string Name, DateTime? Created, int Depth, bool IsNow)
{
    public string CreatedDisplay => Created?.ToString("dd/MM/yyyy HH:mm") ?? "";
    /// <summary>Indent of the row in the list (16 px per level).</summary>
    public double Indent => Depth * 16;
}

/// <summary>Screen of a running VM, converted to BGRA8 pixels.</summary>
public sealed record VmThumbnail(int Width, int Height, byte[] Bgra);

/// <summary>Files removed with a VM: its own disks, checkpoint disks and cloud-init seed, never a shared parent.</summary>
public sealed record VmDeletePlan(string VmFolder, IReadOnlyList<string> Files, IReadOnlyList<string> KeptParents);
