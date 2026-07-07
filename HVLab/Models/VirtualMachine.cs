namespace HVLab.Models;

public class VirtualMachine
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = "Off";
    public long MemoryMB { get; set; }
    public int ProcessorCount { get; set; }
    public string VSwitchName { get; set; } = string.Empty;
    public string BaseVhdxPath { get; set; } = string.Empty;
    public string DifferentialDiskPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
