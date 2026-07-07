namespace HVLab.Models;

public class VirtualMachine
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public long MemoryMB { get; set; }
    public int Generation { get; set; } = 2;
    public string SwitchName { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;

    public bool IsRunning => State == "Running";
    public string MemoryDisplay => $"{MemoryMB} MB";
    public string GenDisplay => $"Gen {Generation}";
    public string StateIcon => IsRunning ? "▶" : "⏹";
}
