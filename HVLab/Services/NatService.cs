using System.Text.Json;
using HVLab.Models;

namespace HVLab.Services;

public class NatService
{
    public async Task<List<NatNetwork>> GetNatNetworksAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                $nats = Get-NetNat -ErrorAction SilentlyContinue | ForEach-Object {
                    [PSCustomObject]@{
                        Name                              = $_.Name
                        InternalIPInterfaceAddressPrefix  = $_.InternalIPInterfaceAddressPrefix
                        Active                            = $_.Active
                    }
                }
                if ($nats) { $nats | ConvertTo-Json -AsArray -Depth 2 } else { '[]' }
            } catch { '[]' }
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
            var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new NatNetwork
                {
                    Name = GetStr(el, "Name"),
                    InternalIPInterfaceAddressPrefix = GetStr(el, "InternalIPInterfaceAddressPrefix"),
                    Active = el.TryGetProperty("Active", out var v) && v.GetBoolean(),
                });
        }
        catch { }
        return result;
    }

    public async Task CreateNatNetworkAsync(string switchName, string natName, string gatewayIP, int prefixLength)
    {
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

            $existingIP = Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue
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

    private static string Esc(string s) => s.Replace("'", "''");

    private static string GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
}
