using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HVLab.Models;
using static HVLab.Services.HyperVService;
using static HVLab.Services.PowerShellRunner;

namespace HVLab.Services;

public class NatService
{
    public async Task<List<NatNetwork>> GetNatNetworksAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                # Partir des vSwitch internes : NomSwitch -> baseIP (ex: "10.0.0")
                $switchSubnetMap = @{}
                Import-Module Hyper-V -ErrorAction SilentlyContinue
                Get-VMSwitch -ErrorAction SilentlyContinue | Where-Object { $_.SwitchType -eq 'Internal' } | ForEach-Object {
                    $adpName = "vEthernet ($($_.Name))"
                    $adp = Get-NetAdapter -Name $adpName -ErrorAction SilentlyContinue
                    if ($adp) {
                        $ip = Get-NetIPAddress -InterfaceIndex $adp.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
                        if ($ip) {
                            $base = $ip.IPAddress -replace '\.\d+$', ''
                            $switchSubnetMap[$base] = $_.Name
                        }
                    }
                }

                $nats = @(Get-NetNat -ErrorAction Stop | ForEach-Object {
                    $nat = $_
                    $switchName = ''
                    try {
                        $base = ($nat.InternalIPInterfaceAddressPrefix -split '/')[0] -replace '\.\d+$', ''
                        if ($switchSubnetMap.ContainsKey($base)) { $switchName = $switchSubnetMap[$base] }
                    } catch {}
                    [PSCustomObject]@{
                        Name                             = $nat.Name
                        InternalIPInterfaceAddressPrefix = $nat.InternalIPInterfaceAddressPrefix
                        Active                           = [bool]$nat.Active
                        SwitchName                       = $switchName
                    }
                })
                if ($nats.Count -gt 0) { ConvertTo-Json -InputObject $nats -Depth 2 } else { '[]' }
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
            """;
        var output = await HyperVService.RunScriptAsync(script);
        return ParseNats(output.Trim());
    }

    private static List<NatNetwork> ParseNats(string json)
    {
        var result = new List<NatNetwork>();
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return result;
        try
        {
            using var doc = JsonDocument.Parse(ExtractJson(json));
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new NatNetwork
                {
                    Name = GetStr(el, "Name"),
                    InternalIPInterfaceAddressPrefix = GetStr(el, "InternalIPInterfaceAddressPrefix"),
                    Active     = el.TryGetProperty("Active",     out var v) && v.ValueKind == JsonValueKind.True,
                    SwitchName = GetStr(el, "SwitchName"),
                });
        }
        catch (JsonException ex) { throw ParseError("NAT", json, ex); }
        return result;
    }

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
            ?? throw new ArgumentException($"Invalid IPv4 gateway or prefix: {gatewayIP}/{prefixLength}");
        var script = $$"""
            $switchName   = '{{Esc(switchName)}}'
            $natName      = '{{Esc(natName)}}'
            $gatewayIP    = '{{gatewayIP}}'
            $prefixLength = {{prefixLength}}
            $prefix       = "$($gatewayIP -replace '\.\d+$', '.0')/$prefixLength"

            $adapter = Get-NetAdapter | Where-Object { $_.Name -eq "vEthernet ($switchName)" }
            if (-not $adapter) {
                throw "Adaptateur introuvable pour le commutateur '$switchName'. Créez d'abord le commutateur interne."
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
        await HyperVService.RunScriptAsync(script);
    }

    public async Task RemoveNatNetworkAsync(string name)
        => await HyperVService.RunScriptAsync(
            $"Remove-NetNat -Name '{Esc(name)}' -Confirm:$false -ErrorAction Stop");

}
