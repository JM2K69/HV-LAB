namespace HVLab.Models;

public class NatNetwork
{
    public string Name { get; set; } = string.Empty;
    public string InternalIPInterfaceAddressPrefix { get; set; } = string.Empty;
    public bool Active { get; set; }

    public string Status => Active ? "Actif" : "Inactif";
}
