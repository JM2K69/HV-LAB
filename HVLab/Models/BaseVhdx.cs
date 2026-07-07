namespace HVLab.Models;

public class BaseVhdx
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeGB { get; set; }
    public bool IsBuilt { get; set; }
    public DateTime? CreatedAt { get; set; }

    public string SizeDisplay => $"{SizeGB} GB";
    public string CreatedAtDisplay => CreatedAt?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
    public string StatusIcon => IsBuilt ? "✓" : "⚠";
}
