using System.Diagnostics;
using System.Text.Json;
using HVLab.Models;
using static HVLab.Services.PowerShellRunner;

namespace HVLab.Services;

public enum HyperVEnvironment { Ok, FeatureMissing, ServiceStopped }

public class HyperVService(IPowerShellRunner ps) : IHyperVService
{
    // ─── PowerShell runner ──────────────────────────────────────────────────

    private Task<string> RunScriptAsync(
        string script, IProgress<string>? progress = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
        => ps.RunAsync(script, progress, timeout, ct);

    /// <summary>PowerShell expression that yields <paramref name="text"/> without any quoting concern (base64 UTF-8).</summary>
    internal static string PsLiteral(string text)
        => $"[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))}'))";

    // ─── Environment ────────────────────────────────────────────────────────

    /// <summary>
    /// Quick host check (no Get-WindowsOptionalFeature, which takes seconds):
    /// Hyper-V PowerShell module present and vmms service running.
    /// </summary>
    public async Task<HyperVEnvironment> GetEnvironmentStatusAsync()
    {
        const string script = """
            if (-not (Get-Module -ListAvailable -Name Hyper-V)) { 'FeatureMissing'; return }
            $svc = Get-Service -Name vmms -ErrorAction SilentlyContinue
            if (-not $svc) { 'FeatureMissing' } elseif ($svc.Status -ne 'Running') { 'ServiceStopped' } else { 'Ok' }
            """;
        var output = (await RunScriptAsync(script, timeout: TimeSpan.FromSeconds(30))).Trim();
        return Enum.TryParse<HyperVEnvironment>(output, out var env) ? env : HyperVEnvironment.Ok;
    }

    public Task StartHyperVServiceAsync()
        => RunScriptAsync("Start-Service -Name vmms -ErrorAction Stop", timeout: TimeSpan.FromSeconds(60));

    // ─── Virtual Machines ───────────────────────────────────────────────────

    public async Task<List<VirtualMachine>> GetVirtualMachinesAsync()
    {
        // $vm.NetworkAdapters is a property already loaded with Get-VM — no extra WMI call per VM.
        // VLAN is fetched via Get-VMNetworkAdapterVlan (one call for all NICs of the VM).
        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                Import-Module Hyper-V -ErrorAction Stop
                $vms = @(Get-VM -ErrorAction Stop | ForEach-Object {
                    $nic     = $_.NetworkAdapters | Select-Object -First 1
                    $vlans   = @($_.NetworkAdapters | ForEach-Object {
                        try {
                            $v = Get-VMNetworkAdapterVlan -VMNetworkAdapter $_ -ErrorAction SilentlyContinue
                            if ($v -and $v.OperationMode -eq 'Access')  { $v.AccessVlanId }
                            elseif ($v -and $v.OperationMode -eq 'Trunk') { ($v.AllowedVlanIdList -join ',') }
                        } catch {}
                    } | Where-Object { $_ -and $_ -ne 0 } | Sort-Object -Unique)
                    $vlanStr = if ($vlans.Count -gt 0) { $vlans -join ', ' } else { '' }
                    [PSCustomObject]@{
                        Name           = $_.Name
                        State          = $_.State.ToString()
                        ProcessorCount = $_.ProcessorCount
                        MemoryMB       = [Math]::Round($_.MemoryStartup / 1MB, 0)
                        Generation     = $_.Generation
                        SwitchName     = if ($nic) { $nic.SwitchName } else { '' }
                        Uptime         = $_.Uptime.ToString()
                        VlanInfo       = $vlanStr
                    }
                })
                if ($vms.Count -gt 0) { ConvertTo-Json -InputObject $vms -Depth 2 -Compress } else { '[]' }
            } catch {
                throw   # rethrow: the runner reports the message
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
            using var doc = JsonDocument.Parse(ExtractJson(json));
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
                    VlanInfo       = GetStr(el, "VlanInfo"),
                });
        }
        catch (JsonException ex) { throw ParseError("VM", json, ex); }
        return result;
    }

    public async Task StartVMAsync(string name)
        => await RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            Start-VM -Name '{{Esc(name)}}' -ErrorAction Stop
            """);

    public async Task StopVMAsync(string name)
        => await RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            Stop-VM -Name '{{Esc(name)}}' -Force -ErrorAction Stop
            $i = 0
            while ((Get-VM -Name '{{Esc(name)}}').State -ne 'Off' -and $i -lt 30) {
                Start-Sleep -Milliseconds 500; $i++
            }
            """);

    public async Task RemoveVMAsync(string name)
        => await RunScriptAsync($"""
            Import-Module Hyper-V -ErrorAction Stop
            Remove-VM -Name '{Esc(name)}' -Force -ErrorAction Stop
            """);

    /// <summary>
    /// Opens the Hyper-V VM Connect window (vmconnect.exe) for the specified VM.
    /// Equivalent to double-clicking the VM in Hyper-V Manager.
    /// </summary>
    public void ConnectToVMConsole(string name)
    {
        var vmconnect = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "vmconnect.exe");

        if (!File.Exists(vmconnect))
            throw new InvalidOperationException(
                LocalizationService.Instance["Err_VmConnect"]);

        var psi = new ProcessStartInfo
        {
            FileName        = vmconnect,
            Arguments       = $"localhost \"{name.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
        };
        Process.Start(psi);
    }

    // ─── Virtual Switches

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
                throw   # rethrow: the runner reports the message
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
            using var doc = JsonDocument.Parse(ExtractJson(json));
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new VirtualSwitch
                {
                    Name              = GetStr(el, "Name"),
                    SwitchType        = GetStr(el, "SwitchType"),
                    Notes             = GetStr(el, "Notes"),
                    NetAdapterName    = GetStr(el, "NetAdapterName"),
                    AllowManagementOS = el.TryGetProperty("AllowManagementOS", out var v) && v.ValueKind == JsonValueKind.True,
                    VlanId            = GetInt(el, "VlanId", 0),
                });
        }
        catch (JsonException ex) { throw ParseError("commutateurs", json, ex); }
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
                throw   # rethrow: the runner reports the message
            }
            """;
        var output = await RunScriptAsync(script);
        var result = new List<NetworkAdapterInfo>();
        if (string.IsNullOrWhiteSpace(output)) return result;
        try
        {
            var json = ExtractJson(output);
            if (json.StartsWith('{')) json = $"[{json}]";
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
                result.Add(new NetworkAdapterInfo
                {
                    Name        = GetStr(el, "Name"),
                    Description = GetStr(el, "Description"),
                    MacAddress  = GetStr(el, "MacAddress"),
                    Status      = GetStr(el, "Status"),
                });
        }
        catch (JsonException ex) { throw ParseError("cartes réseau", output, ex); }
        return result;
    }

    // ─── Create blank VM (new empty VHDX) ───────────────────────────────────

    public async Task CreateBlankVmAsync(
        string vmName, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        long diskSizeGB, bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        InputValidator.EnsureVmName(vmName);
        var isGen2       = generation == 2 ? "$true" : "$false";
        var secureBootPs = secureBoot ? "$true" : "$false";
        var pxeBootPs    = pxeBoot    ? "$true" : "$false";

        var script = $$"""
            $vmName      = '{{Esc(vmName)}}'
            $switchName  = '{{Esc(switchName)}}'
            $memoryBytes = {{memoryMB}}MB
            $cpuCount    = {{cpuCount}}
            $generation  = {{generation}}
            $vmFolder    = '{{Esc(vmFolder)}}'
            $diskBytes   = {{diskSizeGB}}GB
            $isGen2      = {{isGen2}}
            $secureBoot  = {{secureBootPs}}
            $pxeBoot     = {{pxeBootPs}}

            $vmPath    = Join-Path $vmFolder $vmName
            $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
            New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
            New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

            $vhdPath = Join-Path $vhdFolder "$vmName.vhdx"
            New-VHD -Path $vhdPath -SizeBytes $diskBytes -Dynamic -ErrorAction Stop | Out-Null

            New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                   -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop | Out-Null

            Set-VM -Name $vmName -ProcessorCount $cpuCount -ErrorAction Stop
            Add-VMHardDiskDrive -VMName $vmName -Path $vhdPath -ErrorAction Stop

            if ($generation -eq 2) {
                $sbState = if ($secureBoot) { 'On' } else { 'Off' }
                Set-VMFirmware -VMName $vmName -EnableSecureBoot $sbState `
                               -SecureBootTemplate 'MicrosoftWindows' -ErrorAction SilentlyContinue

                $hdd = Get-VMHardDiskDrive -VMName $vmName | Select-Object -First 1
                $nic = Get-VMNetworkAdapter -VMName $vmName | Select-Object -First 1

                if ($pxeBoot -and $nic -and $hdd) {
                    Set-VMFirmware -VMName $vmName -BootOrder $nic,$hdd -ErrorAction Stop
                } elseif ($hdd -and $nic) {
                    Set-VMFirmware -VMName $vmName -BootOrder $hdd,$nic -ErrorAction Stop
                } elseif ($hdd) {
                    Set-VMFirmware -VMName $vmName -FirstBootDevice $hdd -ErrorAction Stop
                } else {
                    throw "##Err_NoBootDisk|$vmName"
                }

                $first = (Get-VMFirmware -VMName $vmName).BootOrder | Select-Object -First 1
                if (-not $pxeBoot -and $first.BootType -eq 'Network') {
                    throw "##Err_BootOrder|$vmName"
                }
            } else {
                if ($pxeBoot) {
                    Set-VMBios -VMName $vmName -StartupOrder @('LegacyNetworkAdapter', 'IDE', 'CD', 'Floppy') -ErrorAction SilentlyContinue
                } else {
                    Set-VMBios -VMName $vmName -StartupOrder @('IDE', 'CD', 'LegacyNetworkAdapter', 'Floppy') -ErrorAction SilentlyContinue
                }
            }
            Write-Output "##Prg_VmCreated|$vmName"
            """;

        await RunScriptAsync(script, progress, ct: ct);
    }

    // ─── Create VM with differencing disk ───────────────────────────────────

    public async Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        string? answerFileContent = null, bool secureBoot = true, bool pxeBoot = false,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        InputValidator.EnsureVmName(vmName);
        // The answer file (it holds the admin password) is embedded in the script streamed over
        // stdin — it is never written to %TEMP%.
        var answerXml = string.IsNullOrWhiteSpace(answerFileContent) ? "''" : PsLiteral(answerFileContent);
        {
            var isGen2      = generation == 2 ? "$true" : "$false";
            var secureBootPs = secureBoot ? "$true" : "$false";
            var pxeBootPs    = pxeBoot    ? "$true" : "$false";
            var script   = $$"""
                $vmName       = '{{Esc(vmName)}}'
                $parentVhdx   = '{{Esc(parentVhdxPath)}}'
                $switchName   = '{{Esc(switchName)}}'
                $memoryBytes  = {{memoryMB}}MB
                $cpuCount     = {{cpuCount}}
                $generation   = {{generation}}
                $vmFolder     = '{{Esc(vmFolder)}}'
                $answerXml    = {{answerXml}}
                $isGen2       = {{isGen2}}
                $secureBoot   = {{secureBootPs}}
                $pxeBoot      = {{pxeBootPs}}

                $vmPath    = Join-Path $vmFolder $vmName
                $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
                New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
                New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

                $diffVhd = Join-Path $vhdFolder "$vmName.vhdx"
                New-VHD -Path $diffVhd -ParentPath $parentVhdx -Differencing -ErrorAction Stop | Out-Null

                # Inject unattend.xml into the differencing disk before first boot
                if ($answerXml) {
                    Write-Output "##Prg_Inject"
                    $mount  = Mount-DiskImage -ImagePath $diffVhd -PassThru -ErrorAction Stop
                    $diskNo = ($mount | Get-Disk).Number
                    # Use a temp folder path instead of a drive letter to avoid triggering Windows AutoPlay / Explorer
                    $mountPath = Join-Path $env:TEMP ("hvlab_inject_" + [System.IO.Path]::GetRandomFileName())
                    New-Item -Path $mountPath -ItemType Directory -Force | Out-Null
                    try {
                        $deadline = (Get-Date).AddSeconds(20)
                        $winPart  = $null
                        do {
                            $winPart = Get-Partition -DiskNumber $diskNo -ErrorAction SilentlyContinue |
                                           Where-Object { $_.Type -eq 'Basic' -and $_.Size -gt 1GB } |
                                           Select-Object -First 1
                            if ($winPart) { break }
                            Start-Sleep -Milliseconds 400
                        } while ((Get-Date) -lt $deadline)

                        if (-not $winPart) { throw "##Err_NoWinPartition" }

                        # Mount to folder — no drive letter, no AutoPlay, no Explorer window
                        $winPart | Add-PartitionAccessPath -AccessPath $mountPath -ErrorAction Stop
                        Start-Sleep -Milliseconds 800   # let the filesystem settle

                        $panther = Join-Path $mountPath 'Windows\Panther'
                        if (-not (Test-Path $panther)) { New-Item -Path $panther -ItemType Directory -Force | Out-Null }
                        [System.IO.File]::WriteAllText((Join-Path $panther 'unattend.xml'), $answerXml, [System.Text.Encoding]::UTF8)
                    } finally {
                        try { $winPart | Remove-PartitionAccessPath -AccessPath $mountPath -ErrorAction SilentlyContinue } catch {}
                        try { Remove-Item $mountPath -Force -ErrorAction SilentlyContinue } catch {}
                        Dismount-DiskImage -ImagePath $diffVhd -ErrorAction SilentlyContinue | Out-Null
                    }
                }

                New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                       -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop | Out-Null

                Set-VM -Name $vmName -ProcessorCount $cpuCount -ErrorAction Stop
                Add-VMHardDiskDrive -VMName $vmName -Path $diffVhd -ErrorAction Stop

                if ($generation -eq 2) {
                    # Secure Boot selon le choix utilisateur
                    $sbState = if ($secureBoot) { 'On' } else { 'Off' }
                    Set-VMFirmware -VMName $vmName -EnableSecureBoot $sbState `
                                   -SecureBootTemplate 'MicrosoftWindows' -ErrorAction SilentlyContinue

                    # Boot order strict: HDD first unless PXE explicitly requested
                    $hdd = Get-VMHardDiskDrive -VMName $vmName | Select-Object -First 1
                    $nic = Get-VMNetworkAdapter -VMName $vmName | Select-Object -First 1
                    if ($pxeBoot -and $nic -and $hdd) {
                        Set-VMFirmware -VMName $vmName -BootOrder $nic,$hdd -ErrorAction Stop
                    } elseif ($hdd -and $nic) {
                        Set-VMFirmware -VMName $vmName -BootOrder $hdd,$nic -ErrorAction Stop
                    } elseif ($hdd) {
                        Set-VMFirmware -VMName $vmName -FirstBootDevice $hdd -ErrorAction Stop
                    } else {
                        throw "##Err_NoBootDisk|$vmName"
                    }

                    $first = (Get-VMFirmware -VMName $vmName).BootOrder | Select-Object -First 1
                    if (-not $pxeBoot -and $first.BootType -eq 'Network') {
                        throw "##Err_BootOrder|$vmName"
                    }
                } else {
                    # Gen1 : BIOS
                    if ($pxeBoot) {
                        Set-VMBios -VMName $vmName -StartupOrder @('LegacyNetworkAdapter', 'IDE', 'CD', 'Floppy') -ErrorAction SilentlyContinue
                    } else {
                        Set-VMBios -VMName $vmName -StartupOrder @('IDE', 'CD', 'LegacyNetworkAdapter', 'Floppy') -ErrorAction SilentlyContinue
                    }
                }
                Write-Output "##Prg_VmCreated|$vmName"
                """;
            await RunScriptAsync(script, progress, ct: ct);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Keeps only the JSON payload (from the first '[' or '{'), ignoring stray output before it.</summary>
    internal static string ExtractJson(string output)
    {
        var text = output.Trim();
        var i = text.IndexOfAny(['[', '{']);
        return i > 0 ? text[i..] : text;
    }

    internal static InvalidOperationException ParseError(string what, string output, Exception inner)
    {
        var snippet = output.Trim();
        if (snippet.Length > 200) snippet = snippet[..200] + "…";
        return new InvalidOperationException(LocalizationService.T("Err_Unreadable", what, snippet), inner);
    }

    internal static string GetStr(JsonElement el, string key)
        => !el.TryGetProperty(key, out var v) ? ""
         : v.ValueKind switch
           {
               JsonValueKind.String => v.GetString() ?? "",
               JsonValueKind.Null or JsonValueKind.Undefined => "",
               _ => v.ToString(),
           };

    internal static int GetInt(JsonElement el, string key, int def = 0)
        => el.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : def;

    internal static long GetLong(JsonElement el, string key, long def = 0)
        => el.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : def;
}
