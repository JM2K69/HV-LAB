namespace HVLab.Models;

public sealed class VmSizeProfile
{
    public string Label       { get; init; } = "";
    public string AccentColor { get; init; } = "";
    public int    CpuCount    { get; init; }
    public long   MemoryMB    { get; init; }
    public string UseCase     { get; init; } = "";

    public string CpuDisplay    => $"{CpuCount} vCPU";
    public string MemoryDisplay => MemoryMB >= 1024 ? $"{MemoryMB / 1024} GB" : $"{MemoryMB} MB";

    public static IReadOnlyList<VmSizeProfile> All { get; } =
    [
        new() { Label = "XXS", AccentColor = "#68768A", CpuCount = 1, MemoryMB = 512,   UseCase = "QVM_UseCase_XXS" },
        new() { Label = "XS",  AccentColor = "#0078D4", CpuCount = 1, MemoryMB = 1024,  UseCase = "QVM_UseCase_XS"  },
        new() { Label = "S",   AccentColor = "#0F7B0F", CpuCount = 2, MemoryMB = 2048,  UseCase = "QVM_UseCase_S"   },
        new() { Label = "M",   AccentColor = "#C19C00", CpuCount = 2, MemoryMB = 4096,  UseCase = "QVM_UseCase_M"   },
        new() { Label = "L",   AccentColor = "#CA5010", CpuCount = 4, MemoryMB = 8192,  UseCase = "QVM_UseCase_L"   },
        new() { Label = "XL",  AccentColor = "#7A00CF", CpuCount = 8, MemoryMB = 16384, UseCase = "QVM_UseCase_XL"  },
    ];
}
