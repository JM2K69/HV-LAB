using System.Diagnostics;
using System.Text.Json;
using HVLab.Models;

namespace HVLab.Services;

public class HyperVService
{
    // ─── PowerShell runner ──────────────────────────────────────────────────

    // Full path to Windows PowerShell (always present on Windows, required for Hyper-V cmdlets).
    // pwsh.exe (PowerShell 7) does NOT have the Hyper-V module; use powershell.exe.
    private static readonly string PowerShellExe =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");

    internal static async Task<string> RunScriptAsync(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hvlab_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(path, script, System.Text.Encoding.UTF8);
            var psi = new ProcessStartInfo
            {
                FileName               = PowerShellExe,
                Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Impossible de démarrer powershell.exe");

            // Read both streams concurrently to prevent deadlocks on large output.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(msg) ? $"PowerShell a retourné le code {proc.ExitCode}." : msg);
            }
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
                Import-Module Hyper-V -ErrorAction Stop
                $vms = @(Get-VM -ErrorAction Stop | ForEach-Object {
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
                })
                if ($vms.Count -gt 0) { ConvertTo-Json -InputObject $vms -Depth 2 } else { '[]' }
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
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
        => await RunScriptAsync($"""
            Import-Module Hyper-V -ErrorAction Stop
            Start-VM -Name '{Esc(name)}' -ErrorAction Stop
            """);

    public async Task StopVMAsync(string name)
        => await RunScriptAsync($"""
            Import-Module Hyper-V -ErrorAction Stop
            Stop-VM -Name '{Esc(name)}' -Force -ErrorAction Stop
            """);

    public async Task RemoveVMAsync(string name)
        => await RunScriptAsync($"""
            Import-Module Hyper-V -ErrorAction Stop
            Remove-VM -Name '{Esc(name)}' -Force -ErrorAction Stop
            """);

    // ─── Virtual Switches ───────────────────────────────────────────────────

    public async Task<List<VirtualSwitch>> GetVirtualSwitchesAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                Import-Module Hyper-V -ErrorAction Stop
                $switches = @(Get-VMSwitch -ErrorAction Stop | ForEach-Object {
                    $vlan = 0
                    try {
                        $vna = Get-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName $_.Name -ErrorAction SilentlyContinue
                        if ($vna -and $vna.OperationMode -eq 'Access') { $vlan = $vna.AccessVlanId }
                    } catch {}
                    [PSCustomObject]@{
                        Name              = $_.Name
                        SwitchType        = $_.SwitchType.ToString()
                        Notes             = if ($_.Notes) { $_.Notes } else { '' }
                        NetAdapterName    = if ($_.NetAdapterName) { $_.NetAdapterName } else { '' }
                        AllowManagementOS = $_.AllowManagementOS
                        VlanId            = $vlan
                    }
                })
                if ($switches.Count -gt 0) { ConvertTo-Json -InputObject $switches -Depth 2 } else { '[]' }
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
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
                    VlanId            = GetInt(el, "VlanId", 0),
                });
        }
        catch { }
        return result;
    }

    public async Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0)
    {
        var vlanScript = vlanId > 0
            ? $"Set-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName '{Esc(name)}' -Access -VlanId {vlanId} -ErrorAction Stop"
            : string.Empty;
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            New-VMSwitch -Name '{{Esc(name)}}' -NetAdapterName '{{Esc(netAdapter)}}' -AllowManagementOS $true -ErrorAction Stop
            {{vlanScript}}
            """;
        await RunScriptAsync(script);
    }

    public async Task CreateInternalSwitchAsync(string name, int vlanId = 0)
    {
        var vlanScript = vlanId > 0
            ? $"Set-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName '{Esc(name)}' -Access -VlanId {vlanId} -ErrorAction Stop"
            : string.Empty;
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            New-VMSwitch -Name '{{Esc(name)}}' -SwitchType Internal -ErrorAction Stop
            {{vlanScript}}
            """;
        await RunScriptAsync(script);
    }

    public async Task CreatePrivateSwitchAsync(string name)
        => await RunScriptAsync(
            $"New-VMSwitch -Name '{Esc(name)}' -SwitchType Private -ErrorAction Stop");

    public async Task RemoveVSwitchAsync(string name)
        => await RunScriptAsync(
            $"Remove-VMSwitch -Name '{Esc(name)}' -Force -ErrorAction Stop");

    // ─── Network Adapters ───────────────────────────────────────────────────

    public async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
    {
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                $adapters = @(Get-NetAdapter -ErrorAction Stop | Where-Object { $_.Status -eq 'Up' } | ForEach-Object {
                    [PSCustomObject]@{
                        Name        = $_.Name
                        Description = $_.InterfaceDescription
                        MacAddress  = $_.MacAddress
                        Status      = $_.Status
                    }
                })
                if ($adapters.Count -gt 0) { ConvertTo-Json -InputObject $adapters -Depth 2 } else { '[]' }
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
            """;
        var output = await RunScriptAsync(script);
        var result = new List<NetworkAdapterInfo>();
        if (string.IsNullOrWhiteSpace(output)) return result;
        try
        {
            var json = output.Trim();
            if (json.StartsWith('{')) json = $"[{json}]";
            var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new NetworkAdapterInfo
                {
                    Name        = GetStr(el, "Name"),
                    Description = GetStr(el, "Description"),
                    MacAddress  = GetStr(el, "MacAddress"),
                    Status      = GetStr(el, "Status"),
                });
        }
        catch { }
        return result;
    }

    // ─── Create VM with differencing disk ───────────────────────────────────

    public async Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        string? answerFileContent = null)
    {
        // Write the answer file to a temp path if provided
        string? answerTemp = null;
        if (!string.IsNullOrWhiteSpace(answerFileContent))
        {
            answerTemp = Path.Combine(Path.GetTempPath(), $"hvlab_unattend_{Guid.NewGuid():N}.xml");
            await File.WriteAllTextAsync(answerTemp, answerFileContent, System.Text.Encoding.UTF8);
        }

        try
        {
            var isGen2   = generation == 2 ? "$true" : "$false";
            var script   = $$"""
                $vmName       = '{{Esc(vmName)}}'
                $parentVhdx   = '{{Esc(parentVhdxPath)}}'
                $switchName   = '{{Esc(switchName)}}'
                $memoryBytes  = {{memoryMB}}MB
                $cpuCount     = {{cpuCount}}
                $generation   = {{generation}}
                $vmFolder     = '{{Esc(vmFolder)}}'
                $answerFile   = '{{Esc(answerTemp ?? "")}}'
                $isGen2       = {{isGen2}}

                $vmPath    = Join-Path $vmFolder $vmName
                $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
                New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
                New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

                $diffVhd = Join-Path $vhdFolder "$vmName.vhdx"
                New-VHD -Path $diffVhd -ParentPath $parentVhdx -Differencing -ErrorAction Stop | Out-Null

                # Inject unattend.xml into the differencing disk before first boot
                if ($answerFile -and (Test-Path $answerFile)) {
                    Write-Output "Injection du fichier de réponse dans le disque différentiel..."
                    $mount = Mount-DiskImage -ImagePath $diffVhd -PassThru -ErrorAction Stop
                    $diskNo = ($mount | Get-Disk).Number
                    try {
                        # Re-query the letter until Windows assigns it (avoids stale-object race condition)
                        $deadline = (Get-Date).AddSeconds(20)
                        $letter   = $null
                        $winPart  = $null
                        do {
                            $parts   = Get-Partition -DiskNumber $diskNo -ErrorAction SilentlyContinue |
                                           Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 1GB }
                            $winPart = $parts | Select-Object -First 1
                            if ($winPart -and -not $winPart.DriveLetter) {
                                $winPart | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction SilentlyContinue
                            }
                            $letter = (Get-Partition -DiskNumber $diskNo -PartitionNumber $winPart.PartitionNumber `
                                           -ErrorAction SilentlyContinue).DriveLetter
                            if ($letter -and $letter -ne "`0") { break }
                            Start-Sleep -Milliseconds 500
                        } while ((Get-Date) -lt $deadline)

                        if ($letter -and $letter -ne "`0") {
                            $panther = "${letter}:\Windows\Panther"
                            if (-not (Test-Path $panther)) { New-Item -Path $panther -ItemType Directory -Force | Out-Null }
                            Copy-Item -Path $answerFile -Destination "$panther\unattend.xml" -Force
                            Write-Output "unattend.xml injecté dans $panther"
                        } else {
                            Write-Warning "Impossible d'obtenir la lettre de la partition Windows — unattend.xml non injecté"
                        }
                    } finally {
                        Dismount-DiskImage -ImagePath $diffVhd -ErrorAction SilentlyContinue | Out-Null
                    }
                }

                New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                       -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop | Out-Null

                Set-VM -Name $vmName -ProcessorCount $cpuCount -ErrorAction Stop
                Add-VMHardDiskDrive -VMName $vmName -Path $diffVhd -ErrorAction Stop

                if ($generation -eq 2) {
                    # Secure Boot activé avec le template Microsoft Windows
                    Set-VMFirmware -VMName $vmName -EnableSecureBoot On `
                                   -SecureBootTemplate 'MicrosoftWindows' -ErrorAction SilentlyContinue

                    # Ordre de boot : Hard Disk en premier, ensuite le reste
                    $hdd     = Get-VMHardDiskDrive -VMName $vmName | Select-Object -First 1
                    $current = (Get-VMFirmware -VMName $vmName).BootOrder
                    $others  = $current | Where-Object { $_.BootType -ne 'Drive' -or $_.Device -isnot [Microsoft.HyperV.PowerShell.HardDiskDrive] }
                    Set-VMFirmware -VMName $vmName -BootOrder (@($hdd) + $others) -ErrorAction SilentlyContinue
                } else {
                    # Gen1 : BIOS - s'assurer que le disque dur est la première entrée de boot
                    Set-VMBios -VMName $vmName -StartupOrder @('IDE', 'CD', 'LegacyNetworkAdapter', 'Floppy') -ErrorAction SilentlyContinue
                }
                Write-Output "VM '$vmName' créée avec succès"
                """;
            await RunScriptAsync(script);
        }
        finally
        {
            if (answerTemp is not null)
                try { File.Delete(answerTemp); } catch { /* ignore */ }
        }
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
