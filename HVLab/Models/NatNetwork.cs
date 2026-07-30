namespace HVLab.Models;

public class NatNetwork
{
    public string Name { get; set; } = string.Empty;
    public string InternalIPInterfaceAddressPrefix { get; set; } = string.Empty;
    public bool   Active     { get; set; }
    public string SwitchName { get; set; } = string.Empty;

    public string Status => Services.LocalizationService.Instance[Active ? "Status_Active" : "Status_Inactive"];
}
