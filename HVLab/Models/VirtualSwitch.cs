namespace HVLab.Models;

public class VirtualSwitch
{
    public string Name { get; set; } = string.Empty;
    public string SwitchType { get; set; } = "NAT";
    public string IpAddress { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int ConnectedVMs { get; set; }
}
