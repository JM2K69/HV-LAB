using System.Net;
using System.Net.Sockets;
using HVLab.Models;
using static HVLab.Services.PowerShellRunner;

namespace HVLab.Services;

public class NatService(IPowerShellRunner ps, IHyperVWmi? wmi = null) : INatService
{
    private readonly IHyperVWmi _wmi = wmi ?? new Wmi.HyperVWmi();

    /// <summary>NAT networks through WMI (MSFT_NetNat), each matched to the internal switch whose host adapter is in its prefix.</summary>
    public Task<List<NatNetwork>> GetNatNetworksAsync() => Task.Run(_wmi.GetNatNetworks);

    /// <summary>Validates an IPv4 gateway and prefix; returns the normalized address or null.</summary>
    public static string? NormalizeGateway(string? gatewayIP, int prefixLength)
    {
        if (prefixLength is < 8 or > 30) return null;
        return IPAddress.TryParse(gatewayIP?.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            ? ip.ToString()
            : null;
    }

    public async Task CreateNatNetworkAsync(string switchName, string natName, string gatewayIP, int prefixLength)
    {
        gatewayIP = NormalizeGateway(gatewayIP, prefixLength)
            ?? throw new ArgumentException(LocalizationService.Instance["SW_ErrInvalidGateway"]);
        var script = $$"""
            $switchName   = '{{Esc(switchName)}}'
            $natName      = '{{Esc(natName)}}'
            $gatewayIP    = '{{gatewayIP}}'
            $prefixLength = {{prefixLength}}
            $prefix       = "$($gatewayIP -replace '\.\d+$', '.0')/$prefixLength"

            $adapter = Get-NetAdapter | Where-Object { $_.Name -eq "vEthernet ($switchName)" }
            if (-not $adapter) {
                throw "##Err_NoAdapter|$switchName"
            }

            $existingIP = Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                          Where-Object { $_.IPAddress -notlike '169.254.*' } |
                          Select-Object -First 1
            if (-not $existingIP) {
                New-NetIPAddress -IPAddress $gatewayIP -PrefixLength $prefixLength `
                                 -InterfaceIndex $adapter.InterfaceIndex -ErrorAction Stop | Out-Null
            }

            $existingNat = Get-NetNat | Where-Object { $_.InternalIPInterfaceAddressPrefix -eq $prefix }
            if ($existingNat) {
                Remove-NetNat -Name $existingNat.Name -Confirm:$false -ErrorAction SilentlyContinue
            }

            New-NetNat -Name $natName -InternalIPInterfaceAddressPrefix $prefix -ErrorAction Stop | Out-Null
            Write-Output "NAT '$natName' créé : $prefix via $gatewayIP"
            """;
        await ps.RunAsync(script);
    }

    public async Task RemoveNatNetworkAsync(string name)
        => await ps.RunAsync(
            $"Remove-NetNat -Name '{Esc(name)}' -Confirm:$false -ErrorAction Stop");

}
