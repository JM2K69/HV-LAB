namespace HVLab.Models;

/// <summary>Static choice lists shared by the VM creation pages.</summary>
public static class LabCatalog
{
    public static IReadOnlyList<string> Languages { get; } = ["fr-FR", "en-US", "en-GB", "de-DE", "es-ES", "it-IT"];

    public static IReadOnlyList<string> TimeZones { get; } =
    [
        "Romance Standard Time", "W. Europe Standard Time", "Central Europe Standard Time",
        "GMT Standard Time", "Eastern Standard Time", "Central Standard Time",
        "Pacific Standard Time", "Turkey Standard Time"
    ];

    public static IReadOnlyList<int>  Generations   { get; } = [1, 2];
    public static IReadOnlyList<long> MemoryOptions { get; } = [512, 1024, 2048, 4096, 8192, 16384];
    public static IReadOnlyList<int>  CpuOptions    { get; } = [1, 2, 4, 8, 12, 16];
}
