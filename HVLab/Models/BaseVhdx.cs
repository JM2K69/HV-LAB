namespace HVLab.Models;

public class BaseVhdx
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string OSType { get; set; } = string.Empty;
    public long SizeGB { get; set; }
    public int UsageCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
