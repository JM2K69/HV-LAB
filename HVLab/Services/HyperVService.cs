using System.Diagnostics;
using System.Text.Json;
using HVLab.Models;

namespace HVLab.Services;

public class HyperVService
{
    internal static async Task<string> RunScriptAsync(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvlab_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(path, script, System.Text.Encoding.UTF8);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Impossible de démarrer powershell.exe");
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                throw new InvalidOperationException(stderr.Trim());
            return stdout;
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    // ─── Virtual Machines ───────────────────────────────────────────────────

    public async Task<List<VirtualMachine>> GetVirtualMachinesAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                $vms = Get-VM -ErrorAction Stop | ForEach-Object {
                    $vm = $_
                    $nic = Get-VMNetworkAdapter -VMName $vm.Name -ErrorAction SilentlyContinue | Select-Object -First 1
                    [PSCustomObject]@{
                        Name           = $vm.Name
                        State          = $vm.State.ToString()
                        ProcessorCount = $vm.ProcessorCount
                        MemoryMB       = [Math]::Round($vm.MemoryStartup / 1MB, 0)
                        Generation     = $vm.Generation
                        SwitchName     = if ($nic) { $nic.SwitchName } else { '' }
                        Uptime         = $vm.Uptime.ToString()
                    }
                }
                if ($vms) { $vms | ConvertTo-Json -AsArray -Depth 2 } else { '[]' }
            } catch { '[]' }
            """;
        var output = await RunScriptAsync(script);
        return ParseVMs(output.Trim());
    }

    private static List<VirtualMachine> ParseVMs(string json)
    {
        var result = new List<VirtualMachine>();
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return result;
        try
        {
            var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new VirtualMachine
                {
                    Name           = GetStr(el, "Name"),
                    State          = GetStr(el, "State"),
                    ProcessorCount = GetInt(el, "ProcessorCount", 1),
                    MemoryMB       = GetLong(el, "MemoryMB"),
                    Generation     = GetInt(el, "Generation", 2),
                    SwitchName     = GetStr(el, "SwitchName"),
                    Uptime         = GetStr(el, "Uptime"),
                });
        }
        catch { /* return empty on parse error */ }
        return result;
    }

    public async Task StartVMAsync(string name)
        => await RunScriptAsync($"Start-VM -Name '{Esc(name)}' -ErrorAction Stop");

    public async Task StopVMAsync(string name)
        => await RunScriptAsync($"Stop-VM -Name '{Esc(name)}' -Force -ErrorAction Stop");

    public async Task RemoveVMAsync(string name)
        => await RunScriptAsync($"Remove-VM -Name '{Esc(name)}' -Force -ErrorAction Stop");

    // ─── Virtual Switches ───────────────────────────────────────────────────

    public async Task<List<VirtualSwitch>> GetVirtualSwitchesAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                $switches = Get-VMSwitch -ErrorAction Stop | ForEach-Object {
                    [PSCustomObject]@{
                        Name             = $_.Name
                        SwitchType       = $_.SwitchType.ToString()
                        Notes            = if ($_.Notes) { $_.Notes } else { '' }
                        NetAdapterName   = if ($_.NetAdapterInterfaceDescription) { $_.NetAdapterInterfaceDescription } else { '' }
                        AllowManagementOS = $_.AllowManagementOS
                    }
                }
                if ($switches) { $switches | ConvertTo-Json -AsArray -Depth 2 } else { '[]' }
            } catch { '[]' }
            """;
        var output = await RunScriptAsync(script);
        return ParseSwitches(output.Trim());
    }

    private static List<VirtualSwitch> ParseSwitches(string json)
    {
        var result = new List<VirtualSwitch>();
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return result;
        try
        {
            var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new VirtualSwitch
                {
                    Name              = GetStr(el, "Name"),
                    SwitchType        = GetStr(el, "SwitchType"),
                    Notes             = GetStr(el, "Notes"),
                    NetAdapterName    = GetStr(el, "NetAdapterName"),
                    AllowManagementOS = el.TryGetProperty("AllowManagementOS", out var v) && v.GetBoolean(),
                });
        }
        catch { }
        return result;
    }

    public async Task CreateExternalSwitchAsync(string name, string netAdapter)
        => await RunScriptAsync(
            $"New-VMSwitch -Name '{Esc(name)}' -NetAdapterName '{Esc(netAdapter)}' -AllowManagementOS $true -ErrorAction Stop");

    public async Task CreateInternalSwitchAsync(string name)
        => await RunScriptAsync(
            $"New-VMSwitch -Name '{Esc(name)}' -SwitchType Internal -ErrorAction Stop");

    public async Task CreatePrivateSwitchAsync(string name)
        => await RunScriptAsync(
            $"New-VMSwitch -Name '{Esc(name)}' -SwitchType Private -ErrorAction Stop");

    public async Task RemoveVSwitchAsync(string name)
        => await RunScriptAsync(
            $"Remove-VMSwitch -Name '{Esc(name)}' -Force -ErrorAction Stop");

    // ─── Network Adapters ───────────────────────────────────────────────────

    public async Task<List<string>> GetNetworkAdaptersAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | Select-Object -ExpandProperty Name | ConvertTo-Json -AsArray
            """;
        var output = await RunScriptAsync(script);
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(output)) return result;
        try
        {
            var doc = JsonDocument.Parse(output.Trim());
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var n = el.GetString();
                if (!string.IsNullOrEmpty(n)) result.Add(n);
            }
        }
        catch { }
        return result;
    }

    // ─── Create VM with differencing disk ───────────────────────────────────

    public async Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder)
    {
        var script = $$"""
            $vmName       = '{{Esc(vmName)}}'
            $parentVhdx   = '{{Esc(parentVhdxPath)}}'
            $switchName   = '{{Esc(switchName)}}'
            $memoryBytes  = {{memoryMB}}MB
            $cpuCount     = {{cpuCount}}
            $generation   = {{generation}}
            $vmFolder     = '{{Esc(vmFolder)}}'

            $vmPath    = Join-Path $vmFolder $vmName
            $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
            New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
            New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

            $diffVhd = Join-Path $vhdFolder "$vmName.vhdx"
            New-VHD -Path $diffVhd -ParentPath $parentVhdx -Differencing -ErrorAction Stop | Out-Null

            New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                   -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop | Out-Null

            Set-VM -Name $vmName -ProcessorCount $cpuCount -ErrorAction Stop
            Add-VMHardDiskDrive -VMName $vmName -Path $diffVhd -ErrorAction Stop

            if ($generation -eq 2) {
                Set-VMFirmware -VMName $vmName -EnableSecureBoot Off -ErrorAction SilentlyContinue
            }
            Write-Output "VM '$vmName' créée avec succès"
            """;
        await RunScriptAsync(script);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string Esc(string s) => s.Replace("'", "''");

    private static string GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string key, int def = 0)
        => el.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : def;

    private static long GetLong(JsonElement el, string key, long def = 0)
        => el.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : def;
}
