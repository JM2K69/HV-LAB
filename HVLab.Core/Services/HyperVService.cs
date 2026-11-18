using System.Diagnostics;
using System.Text.Json;
using HVLab.Models;
using static HVLab.Services.PowerShellRunner;

namespace HVLab.Services;

public enum HyperVEnvironment { Ok, FeatureMissing, ServiceStopped }

/// <summary>
/// Hyper-V operations. Frequent ones (VM list, power actions, screen, keyboard, checkpoint list, switches)
/// go through WMI (<see cref="IHyperVWmi"/>), heavy and rare ones (VM creation, disks, settings, groups)
/// through the PowerShell pool.
/// </summary>
public partial class HyperVService(IPowerShellRunner ps, IHyperVWmi? wmi = null) : IHyperVService, IDisposable
{
    private readonly IHyperVWmi _wmi = wmi ?? new Wmi.HyperVWmi();

    /// <summary>Singleton: the container disposes it when the app exits (cached WMI keyboard devices).</summary>
    public void Dispose()
    {
        (_wmi as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>WMI calls are synchronous: run them off the UI thread.</summary>
    private static Task<T> Wmi<T>(Func<T> call) => Task.Run(call);
    private static Task Wmi(Action call) => Task.Run(call);

    // ─── PowerShell runner ──────────────────────────────────────────────────

    /// <summary>
    /// Every script has a deadline: a hung cmdlet (a stuck WMI provider, a disk that never mounts) would otherwise keep
    /// its worker forever, and three of them freeze the whole pool. Creation gets <see cref="CreationTimeout"/>.
    /// </summary>
    internal static readonly TimeSpan DefaultScriptTimeout = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan CreationTimeout     = TimeSpan.FromMinutes(60);

    /// <summary>Runs a script with its deadline and writes one line to the operation log (name, duration, result).</summary>
    private async Task<string> RunScriptAsync(
        string script, IProgress<string>? progress = null,
        TimeSpan? timeout = null, CancellationToken ct = default,
        [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var output = await ps.RunAsync(script, progress, timeout ?? DefaultScriptTimeout, ct);
            CrashLogger.Operation(operation, watch.Elapsed);
            return output;
        }
        catch (Exception ex)
        {
            CrashLogger.Operation(operation, watch.Elapsed, ex);
            throw;
        }
        finally { InvalidateSnapshot(); }   // a script may have changed VMs (cheap when it did not)
    }

    /// <summary>
    /// Settings of every VM HV-LAB creates, chosen for labs (one line, inserted after New-VM / Set-VM):
    /// no automatic checkpoint (Windows client hosts add one at each start, an .avhdx layer nobody asked for),
    /// shut down with the host instead of saving a memory-sized state file, and the Guest Service Interface on
    /// (Copy-VMFile). Older hosts without one of these settings keep their default (SilentlyContinue).
    /// The integration service is found by its ID: its display name is translated on non-English hosts.
    /// </summary>
    internal const string LabDefaults =
        "Set-VM -VM $vm -AutomaticCheckpointsEnabled $false -AutomaticStopAction ShutDown -ErrorAction SilentlyContinue; " +
        "Get-VMIntegrationService -VM $vm | Where-Object { $_.Id -match '6C09BB55-D683-4DA0-8931-C9BF705F6480' } | " +
        "Enable-VMIntegrationService -ErrorAction SilentlyContinue";

    /// <summary>
    /// Runs a creation script; when it fails, times out or is cancelled (a cancel kills the worker, so the script's own
    /// finally blocks never run), removes what this run left behind before rethrowing.
    /// </summary>
    private async Task RunCreationScriptAsync(string script, string vmName, string vmFolder,
                                              IProgress<string>? progress, CancellationToken ct,
                                              [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        var started = DateTime.UtcNow;
        try { await RunScriptAsync(script, progress, CreationTimeout, ct, operation); }
        catch
        {
            try { await RunScriptAsync(CleanupScript(vmName, vmFolder, started), timeout: TimeSpan.FromMinutes(5), operation: "CleanupFailedCreation"); }
            catch (Exception cleanup) { CrashLogger.Log($"Cleanup after the creation of {vmName}", cleanup); }
            throw;
        }
    }

    /// <summary>
    /// Undoes a failed creation, touching only what was created after <paramref name="startedUtc"/>: a namesake VM or an
    /// older folder of the same name is never removed. Disks of the VM folder are dismounted first (unattend or seed
    /// injection interrupted), then the VM and the folder are removed.
    /// </summary>
    internal static string CleanupScript(string vmName, string vmFolder, DateTime startedUtc) => $$"""
        Import-Module Hyper-V -ErrorAction Stop
        $vmName  = '{{Esc(vmName)}}'
        $vmPath  = Join-Path '{{Esc(vmFolder)}}' $vmName
        $started = [DateTime]::Parse('{{startedUtc.AddSeconds(-2):o}}', $null, 'RoundtripKind').ToLocalTime()
        foreach ($vm in @(Get-VM | Where-Object { $_.Name -eq $vmName -and $_.CreationTime -ge $started })) {
            Stop-VM -VM $vm -TurnOff -Force -ErrorAction SilentlyContinue
            Remove-VM -VM $vm -Force -ErrorAction SilentlyContinue
        }
        if ((Test-Path -LiteralPath $vmPath) -and (Get-Item -LiteralPath $vmPath).CreationTime -ge $started) {
            Get-ChildItem -LiteralPath $vmPath -Recurse -File -Include *.vhdx, *.avhdx |
                ForEach-Object { Dismount-DiskImage -ImagePath $_.FullName -ErrorAction SilentlyContinue | Out-Null }
            Remove-Item -LiteralPath $vmPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        """;

    /// <summary>
    /// Empty folders left in ProgramData\HV-LAB\mnt by an interrupted injection (called at startup). A folder that is
    /// still a mount point (reparse point) is left alone: deleting it could reach into the mounted volume.
    /// </summary>
    public static void PurgeMountFolders()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HV-LAB", "mnt");
        if (!Directory.Exists(root)) return;
        foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
        {
            try
            {
                if (!dir.Attributes.HasFlag(FileAttributes.ReparsePoint) && !dir.EnumerateFileSystemInfos().Any())
                    dir.Delete();
            }
            catch (Exception ex) { CrashLogger.Log($"Mount folder {dir.Name}", ex); }
        }
    }

    /// <summary>PowerShell expression that yields <paramref name="text"/> without any quoting concern (base64 UTF-8).</summary>
    internal static string PsLiteral(string text)
        => $"[System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text))}'))";

    // Multi-line, and may hold a password: embedded like the answer file
    private static string NotesLiteral(string? notes) => string.IsNullOrEmpty(notes) ? "''" : PsLiteral(notes);

    public Task SetNotesAsync(string vmId, string notes)
        => RunScriptAsync($"Set-VM -VM {VmExpr(vmId)} -Notes ({NotesLiteral(notes)}) -ErrorAction Stop");

    // ─── Environment ────────────────────────────────────────────────────────

    /// <summary>Quick host check through WMI: Hyper-V PowerShell module present and vmms service running.</summary>
    public Task<HyperVEnvironment> GetEnvironmentStatusAsync() => Wmi(_wmi.GetEnvironment);

    public Task StartHyperVServiceAsync()
        => RunScriptAsync("Start-Service -Name vmms -ErrorAction Stop", timeout: TimeSpan.FromSeconds(60));

    // ─── Virtual Machines ───────────────────────────────────────────────────

    // ─── VM list shared by every view ───────────────────────────────────────
    // The VM list page and each VM window poll every 3 s: they share one WMI enumeration instead of one each.
    // A snapshot younger than SnapshotMaxAge is reused, concurrent callers wait for the same query, every caller
    // gets its own copies (the views change their rows), and any change made through this service drops it.

    internal static readonly TimeSpan SnapshotMaxAge = TimeSpan.FromSeconds(2.5);
    private readonly object _snapshotLock = new();
    private Task<List<VirtualMachine>>? _snapshot;
    private DateTime _snapshotAt;

    public async Task<List<VirtualMachine>> GetVirtualMachinesAsync()
    {
        Task<List<VirtualMachine>> query;
        lock (_snapshotLock)
        {
            if (_snapshot is null || _snapshot.IsFaulted || _snapshot.IsCanceled || DateTime.UtcNow - _snapshotAt > SnapshotMaxAge)
            {
                _snapshot   = Wmi(_wmi.GetVirtualMachines);
                _snapshotAt = DateTime.UtcNow;
            }
            query = _snapshot;
        }
        return (await query).Select(v => v.Copy()).ToList();
    }

    // ─── Guest automation (PowerShell Direct, Copy-VMFile) ──────────────────

    /// <summary>
    /// Runs a script in a Windows guest through PowerShell Direct (VMBus: no network, no firewall rule needed).
    /// The account, the password and the script reach PowerShell as base64 literals: no quoting issue, nothing on disk.
    /// </summary>
    public async Task<string> InvokeInGuestAsync(string vmId, string account, string password, string script)
    {
        var output = await RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm   = {{VmExpr(vmId)}}
            $pass = ConvertTo-SecureString ({{PsLiteral(password)}}) -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential (({{PsLiteral(account)}}), $pass)
            $code = [scriptblock]::Create(({{PsLiteral(script)}}))
            Invoke-Command -VMId $vm.Id -Credential $cred -ScriptBlock $code -ErrorAction Stop | Out-String -Width 200
            """, timeout: TimeSpan.FromMinutes(10));
        return output.Trim();
    }

    /// <summary>Copies a host file into the guest folder (needs the Guest Service Interface, on for HV-LAB VMs).</summary>
    public Task CopyToGuestAsync(string vmId, string sourcePath, string guestFolder)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm = {{VmExpr(vmId)}}
            Get-VMIntegrationService -VM $vm | Where-Object { $_.Id -match '6C09BB55-D683-4DA0-8931-C9BF705F6480' } |
                Enable-VMIntegrationService -ErrorAction SilentlyContinue
            $source = '{{Esc(sourcePath)}}'
            $target = Join-Path '{{Esc(guestFolder)}}' (Split-Path $source -Leaf)
            Copy-VMFile -VM $vm -SourcePath $source -DestinationPath $target -FileSource Host -CreateFullPath -Force -ErrorAction Stop
            """, timeout: TimeSpan.FromMinutes(30));

    public Task<HostCapacity> GetHostCapacityAsync()
        => Wmi(() => _wmi.GetHostCapacity([AppSettings.Current.VmsFolder, AppSettings.Current.BaseImagesFolder]));

    /// <summary>The next list read goes to WMI again (called after any change: power, script, creation…).</summary>
    private void InvalidateSnapshot()
    {
        lock (_snapshotLock) _snapshot = null;
    }

    // A power action can wait minutes for its job (shutdown, save): at most 4 at once, so a bulk action
    // on 20 VMs never ties up 20 pool threads. The others wait asynchronously for a slot.
    private static readonly SemaphoreSlim PowerSlots = new(4, 4);

    public async Task PowerAsync(string vmId, VmPowerAction action)
    {
        await PowerSlots.WaitAsync();
        var watch = Stopwatch.StartNew();
        try
        {
            await Wmi(() => _wmi.ChangeState(vmId, action));
            CrashLogger.Operation($"Power {action}", watch.Elapsed);
        }
        catch (Exception ex)
        {
            CrashLogger.Operation($"Power {action}", watch.Elapsed, ex);
            throw;
        }
        finally
        {
            PowerSlots.Release();
            InvalidateSnapshot();
        }
    }

    /// <summary>
    /// PowerShell expression of exactly one VM. The UI passes the Hyper-V GUID (the WMI id): that VM and no other,
    /// even with namesakes. A name is matched literally (never as a wildcard) and must be unique.
    /// </summary>
    internal static string VmExpr(string vmIdOrName)
    {
        if (Guid.TryParse(vmIdOrName, out var id)) return $"(Get-VM -Id '{id}' -ErrorAction Stop)";
        InputValidator.EnsureVmName(vmIdOrName);
        var name = Esc(vmIdOrName);
        return "$($hvlMatch = @(Get-VM | Where-Object Name -eq '" + name + "'); " +
               "if ($hvlMatch.Count -ne 1) { throw ('##Err_VmNotUnique|' + '" + name + "' + '|' + $hvlMatch.Count) }; $hvlMatch[0])";
    }

    /// <summary>
    /// PowerShell expression of exactly one switch: by its GUID (what the UI passes), else by a name matched literally
    /// that must be unique. Hyper-V accepts two switches with the same name, and Remove-VMSwitch -Name removes both.
    /// </summary>
    internal static string SwitchExpr(string switchIdOrName)
    {
        if (Guid.TryParse(switchIdOrName, out var id)) return $"(Get-VMSwitch -Id '{id}' -ErrorAction Stop)";
        var name = Esc(switchIdOrName);
        return "$($hvlSw = @(Get-VMSwitch | Where-Object Name -eq '" + name + "'); " +
               "if ($hvlSw.Count -ne 1) { throw ('##Err_SwitchNotUnique|' + '" + name + "' + '|' + $hvlSw.Count) }; $hvlSw[0])";
    }

    /// <summary>Refuses a switch name already used: a namesake would make every later action by name ambiguous.</summary>
    private static string NoSwitchNamesake(string name)
        => $"if (@(Get-VMSwitch | Where-Object Name -eq '{Esc(name)}').Count -gt 0) {{ throw '##Err_SwitchExists|{Esc(name)}' }}";

    public async Task<VmDeletePlan> GetDeletePlanAsync(string name)
    {
        // The VM's own disks and checkpoint disks. Differencing parents are followed only while they stay inside
        // the VM folder: the base image, a disk another VM uses and anything in the base images folder are kept.
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm      = {{VmExpr(name)}}
            $vmPath  = "$($vm.Path)".TrimEnd('\')
            $baseDir = '{{Esc(AppSettings.Current.BaseImagesFolder)}}'.TrimEnd('\').ToLowerInvariant()
            $others  = @(Get-VM | Where-Object { $_.Id -ne $vm.Id } | ForEach-Object {
                           @(Get-VMHardDiskDrive -VM $_) + @(Get-VMSnapshot -VM $_ | Get-VMHardDiskDrive) } |
                         Where-Object Path | ForEach-Object { $_.Path.ToLowerInvariant() })
            $roots = @(@(Get-VMHardDiskDrive -VM $vm) + @(Get-VMSnapshot -VM $vm | Get-VMHardDiskDrive) |
                       Where-Object Path | ForEach-Object Path | Select-Object -Unique)
            $files = [System.Collections.Generic.List[string]]::new()
            $kept  = [System.Collections.Generic.List[string]]::new()
            foreach ($root in $roots) {
                $p = $root; $first = $true
                while ($p) {
                    $lower  = $p.ToLowerInvariant()
                    $inside = $lower.StartsWith($vmPath.ToLowerInvariant() + '\') -or $lower.EndsWith('.avhdx')
                    $shared = ($others -contains $lower) -or ($baseDir -and $lower.StartsWith($baseDir + '\'))
                    if ($shared -or (-not $first -and -not $inside)) { if (-not $kept.Contains($p)) { $kept.Add($p) }; break }
                    if (-not (Test-Path -LiteralPath $p)) { break }
                    if (-not $files.Contains($p)) { $files.Add($p) }
                    $first = $false
                    $p = (Get-VHD -Path $p -ErrorAction SilentlyContinue).ParentPath
                }
            }
            [PSCustomObject]@{ VmFolder = $vmPath; Files = @($files); KeptParents = @($kept) } | ConvertTo-Json -Depth 3 -Compress
            """;
        var json = ExtractJson(await RunScriptAsync(script));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new VmDeletePlan(GetStr(root, "VmFolder"), Strings(root, "Files"), Strings(root, "KeptParents"));
        }
        catch (JsonException ex) { throw ParseError("disques", json, ex); }
    }

    /// <summary>JSON array of strings (a single string when PowerShell unrolled a one-item array).</summary>
    private static List<string> Strings(JsonElement e, string name)
        => !e.TryGetProperty(name, out var v) ? []
         : v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
         : v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? [s] : [];

    public async Task RemoveVMAsync(string name, IReadOnlyList<string>? files = null)
    {
        var fileList = files is { Count: > 0 } ? "@(" + string.Join(", ", files.Select(f => $"'{Esc(f)}'")) + ")" : "@()";
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm     = {{VmExpr(name)}}
            $vmPath = "$($vm.Path)"
            $files  = {{fileList}}
            # The elevated delete must never follow a junction or link that someone put in the path (it would delete
            # files elsewhere): every listed file, and each folder from it up to the VM folder, must be a plain one.
            $vmRoot = (Split-Path "$vmPath".TrimEnd('\') -Parent)
            foreach ($f in $files) {
                $item = Get-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
                while ($item) {
                    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "##Err_ReparsePoint|$($item.FullName)" }
                    $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
                    if ($item -and $vmRoot -and $item.FullName.TrimEnd('\') -ieq $vmRoot.TrimEnd('\')) { break }
                }
            }
            if ($vm.State -ne 'Off') { Stop-VM -VM $vm -TurnOff -Force -ErrorAction Stop }
            Remove-VM -VM $vm -Force -ErrorAction Stop
            foreach ($f in $files) {
                # Hyper-V can keep a disk open for a moment after Remove-VM
                for ($i = 0; $i -lt 20 -and (Test-Path -LiteralPath $f); $i++) {
                    try { Remove-Item -LiteralPath $f -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 }
                }
                if (Test-Path -LiteralPath $f) { throw "##Err_FileLocked|$f" }
            }
            # Empty folders of an HV-LAB VM folder (<VMs folder>\<name>): never the Hyper-V default folder
            if ($files.Count -gt 0 -and $vmPath -and (Split-Path $vmPath -Leaf) -eq $vm.Name -and (Test-Path -LiteralPath $vmPath)) {
                Get-ChildItem -LiteralPath $vmPath -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending |
                    Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } |
                    Remove-Item -Force -ErrorAction SilentlyContinue
                if (-not (Get-ChildItem -LiteralPath $vmPath -Force)) { Remove-Item -LiteralPath $vmPath -Force -ErrorAction SilentlyContinue }
            }
            """;
        await RunScriptAsync(script);
    }

    // ─── Detail pane: screen, keyboard ──────────────────────────────────────

    public Task<VmThumbnail?> GetThumbnailAsync(string vmId, int maxWidth, int maxHeight)
        => Wmi(() => _wmi.GetThumbnail(vmId, maxWidth, maxHeight));

    public Task TypeTextAsync(string vmId, string text, string language, bool pressEnter)
        => Wmi(() =>
        {
            var codes = KeyboardScancodes.Build(text, KeyboardScancodes.Klid(language), pressEnter);
            // Short batches: the virtual keyboard takes a bounded scancode buffer
            foreach (var chunk in codes.Chunk(64)) _wmi.TypeScancodes(vmId, chunk);
        });

    public Task SendCtrlAltDelAsync(string vmId) => Wmi(() => _wmi.TypeCtrlAltDel(vmId));

    public Task<bool> GetEnhancedSessionHostAsync() => Wmi(_wmi.GetEnhancedSessionHost);

    public Task SetEnhancedSessionHostAsync(bool enabled)
        => RunScriptAsync($"Import-Module Hyper-V -ErrorAction Stop\nSet-VMHost -EnableEnhancedSessionMode ${(enabled ? "true" : "false")} -ErrorAction Stop");

    // ─── Checkpoints: list through WMI, changes through PowerShell ──────────

    public Task<List<CheckpointRow>> GetCheckpointsAsync(string vmId) => Wmi(() => _wmi.GetCheckpoints(vmId));

    public Task CreateCheckpointAsync(string vmName, string checkpointName)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            Checkpoint-VM -VM {{VmExpr(vmName)}} -SnapshotName '{{Esc(checkpointName)}}' -ErrorAction Stop
            """);

    /// <summary>Checkpoints are addressed by ID: names can repeat.</summary>
    private Task CheckpointScriptAsync(string vmName, string checkpointId, string action)
    {
        var id = Guid.Parse(checkpointId);
        return RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm = {{VmExpr(vmName)}}
            $cp = Get-VMSnapshot -VM $vm -ErrorAction Stop | Where-Object { $_.Id -eq '{{id}}' } | Select-Object -First 1
            if (-not $cp) { throw "##Err_CheckpointNotFound" }
            {{action}}
            """);
    }

    public Task ApplyCheckpointAsync(string vmName, string checkpointId)
        => CheckpointScriptAsync(vmName, checkpointId, """
            $wasRunning = $vm.State -eq 'Running'
            if ($vm.State -ne 'Off') { Stop-VM -VM $vm -TurnOff -Force -ErrorAction Stop }
            Restore-VMSnapshot -VMSnapshot $cp -Confirm:$false -ErrorAction Stop
            if ($wasRunning -and (Get-VM -Id $vm.Id).State -ne 'Running') { Start-VM -VM $vm -ErrorAction Stop }
            """);

    public Task RemoveCheckpointAsync(string vmName, string checkpointId, bool withChildren)
        => CheckpointScriptAsync(vmName, checkpointId, withChildren
            ? "Remove-VMSnapshot -VMSnapshot $cp -IncludeAllChildSnapshots -Confirm:$false -ErrorAction Stop"
            : "Remove-VMSnapshot -VMSnapshot $cp -Confirm:$false -ErrorAction Stop");

    public Task RenameCheckpointAsync(string vmName, string checkpointId, string newName)
        => CheckpointScriptAsync(vmName, checkpointId,
            $"Rename-VMSnapshot -VMSnapshot $cp -NewName '{Esc(newName)}' -ErrorAction Stop");

    // ─── Settings after creation ────────────────────────────────────────────

    public async Task<VmSettings> GetVmSettingsAsync(string vmName)
    {
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            $vm   = {{VmExpr(vmName)}}
            $mem  = Get-VMMemory -VM $vm
            $nic  = @(Get-VMNetworkAdapter -VM $vm) | Select-Object -First 1
            $vlan = if ($nic) { Get-VMNetworkAdapterVlan -VMNetworkAdapter $nic } else { $null }
            $dvd  = @(Get-VMDvdDrive -VM $vm) | Select-Object -First 1
            [PSCustomObject]@{
                ProcessorCount = $vm.ProcessorCount
                Nested         = [bool](Get-VMProcessor -VM $vm).ExposeVirtualizationExtensions
                StartupMB      = [int]($mem.Startup / 1MB)
                DynamicMemory  = [bool]$mem.DynamicMemoryEnabled
                MinimumMB      = [int]($mem.Minimum / 1MB)
                MaximumMB      = [int]($mem.Maximum / 1MB)
                IsoPath        = if ($dvd -and $dvd.Path) { "$($dvd.Path)" } else { '' }
                SwitchName     = if ($nic -and $nic.SwitchName) { "$($nic.SwitchName)" } else { '' }
                VlanMode       = if ($vlan -and "$($vlan.OperationMode)" -in 'Access', 'Trunk') { "$($vlan.OperationMode)" } else { 'Untagged' }
                VlanId         = if ($vlan -and "$($vlan.OperationMode)" -eq 'Access') { [int]$vlan.AccessVlanId } else { 0 }
                NativeVlanId   = if ($vlan -and "$($vlan.OperationMode)" -eq 'Trunk') { [int]$vlan.NativeVlanId } else { 0 }
                AllowedVlans   = if ($vlan -and "$($vlan.OperationMode)" -eq 'Trunk') { "$($vlan.AllowedVlanIdListString)" } else { '' }
                DhcpGuard      = $nic -and "$($nic.DhcpGuard)" -eq 'On'
                RouterGuard    = $nic -and "$($nic.RouterGuard)" -eq 'On'
                MacSpoofing    = $nic -and "$($nic.MacAddressSpoofing)" -eq 'On'
                IsRunning      = "$($vm.State)" -ne 'Off'
            } | ConvertTo-Json -Compress
            """;
        var json = ExtractJson(await RunScriptAsync(script));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            static bool B(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            return new VmSettings
            {
                ProcessorCount = GetInt(r, "ProcessorCount", 1),
                Nested         = B(r, "Nested"),
                StartupMB      = GetInt(r, "StartupMB"),
                DynamicMemory  = B(r, "DynamicMemory"),
                MinimumMB      = GetInt(r, "MinimumMB"),
                MaximumMB      = GetInt(r, "MaximumMB"),
                IsoPath        = GetStr(r, "IsoPath"),
                SwitchName     = GetStr(r, "SwitchName"),
                VlanMode       = GetStr(r, "VlanMode") is { Length: > 0 } mode ? mode : VmSettings.VlanUntagged,
                VlanId         = GetInt(r, "VlanId", 0),
                NativeVlanId   = GetInt(r, "NativeVlanId", 0),
                AllowedVlans   = GetStr(r, "AllowedVlans"),
                DhcpGuard      = B(r, "DhcpGuard"),
                RouterGuard    = B(r, "RouterGuard"),
                MacSpoofing    = B(r, "MacSpoofing"),
                IsRunning      = B(r, "IsRunning"),
            };
        }
        catch (JsonException ex) { throw ParseError("paramètres", json, ex); }
    }

    /// <summary>Applies only what changed; processor and memory only when the VM was off (see <see cref="VmSettings"/>).</summary>
    public Task SetVmSettingsAsync(string vmName, VmSettings before, VmSettings after)
    {
        var lines = SettingsScript(before, after);
        return lines.Count == 0 ? Task.CompletedTask
            : RunScriptAsync($"Import-Module Hyper-V -ErrorAction Stop\n$vm = {VmExpr(vmName)}\n" + string.Join("\n", lines));
    }

    internal static List<string> SettingsScript(VmSettings before, VmSettings after)
    {
        var lines = new List<string>();
        var nic = "Get-VMNetworkAdapter -VM $vm | Select-Object -First 1";
        if (!before.IsRunning)
        {
            if (after.ProcessorCount != before.ProcessorCount || after.Nested != before.Nested)
                lines.Add($"Set-VMProcessor -VM $vm -Count {after.ProcessorCount} -ExposeVirtualizationExtensions ${(after.Nested ? "true" : "false")} -ErrorAction Stop");
            if (after.StartupMB != before.StartupMB || after.DynamicMemory != before.DynamicMemory
                || (after.DynamicMemory && (after.MinimumMB != before.MinimumMB || after.MaximumMB != before.MaximumMB)))
                lines.Add(after.DynamicMemory
                    ? $"Set-VMMemory -VM $vm -DynamicMemoryEnabled $true -StartupBytes {after.StartupMB}MB -MinimumBytes {after.MinimumMB}MB -MaximumBytes {after.MaximumMB}MB -ErrorAction Stop"
                    : $"Set-VMMemory -VM $vm -DynamicMemoryEnabled $false -StartupBytes {after.StartupMB}MB -ErrorAction Stop");
        }
        if (after.IsoPath != before.IsoPath)
            lines.Add(after.IsoPath.Length == 0
                ? "Get-VMDvdDrive -VM $vm | Set-VMDvdDrive -Path $null -ErrorAction Stop"
                : $"$dvd = @(Get-VMDvdDrive -VM $vm) | Select-Object -First 1; " +
                  $"if ($dvd) {{ Set-VMDvdDrive -VMDvdDrive $dvd -Path '{Esc(after.IsoPath)}' -ErrorAction Stop }} " +
                  $"else {{ Add-VMDvdDrive -VM $vm -Path '{Esc(after.IsoPath)}' -ErrorAction Stop }}");
        if (after.SwitchName != before.SwitchName && after.SwitchName.Length > 0)
            lines.Add($"{nic} | Connect-VMNetworkAdapter -SwitchName '{Esc(after.SwitchName)}' -ErrorAction Stop");
        if (after.VlanMode != before.VlanMode || after.VlanId != before.VlanId
            || after.NativeVlanId != before.NativeVlanId || after.AllowedVlans != before.AllowedVlans)
            lines.Add(after.VlanMode switch
            {
                VmSettings.VlanAccess => $"{nic} | Set-VMNetworkAdapterVlan -Access -VlanId {after.VlanId} -ErrorAction Stop",
                VmSettings.VlanTrunk  => $"{nic} | Set-VMNetworkAdapterVlan -Trunk -NativeVlanId {after.NativeVlanId} " +
                                         $"-AllowedVlanIdList '{Esc(after.AllowedVlans.Replace(" ", ""))}' -ErrorAction Stop",
                _                     => $"{nic} | Set-VMNetworkAdapterVlan -Untagged -ErrorAction Stop",
            });
        // Port guards; MAC spoofing is also what nested virtualization needs (switched on with it)
        var guards = new List<string>();
        if (after.DhcpGuard   != before.DhcpGuard)   guards.Add($"-DhcpGuard {(after.DhcpGuard ? "On" : "Off")}");
        if (after.RouterGuard != before.RouterGuard) guards.Add($"-RouterGuard {(after.RouterGuard ? "On" : "Off")}");
        if (after.MacSpoofing != before.MacSpoofing) guards.Add($"-MacAddressSpoofing {(after.MacSpoofing ? "On" : "Off")}");
        if (guards.Count > 0)
            lines.Add($"{nic} | Set-VMNetworkAdapter {string.Join(" ", guards)} -ErrorAction Stop");
        return lines;
    }

    // ─── Hyper-V VM groups ──────────────────────────────────────────────────

    private static string VmList(IReadOnlyList<string> vmNames)
    {
        return "@(" + string.Join(", ", vmNames.Select(VmExpr)) + ")";
    }

    public Task AddToGroupAsync(string groupName, IReadOnlyList<string> vmNames)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $group = Get-VMGroup -Name '{{Esc(groupName)}}' -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $group) { $group = New-VMGroup -Name '{{Esc(groupName)}}' -GroupType VMCollectionType -ErrorAction Stop }
            foreach ($vm in {{VmList(vmNames)}}) {
                if (-not ($group.VMMembers | Where-Object { $_.Id -eq $vm.Id })) { Add-VMGroupMember -VMGroup $group -VM $vm -ErrorAction Stop }
            }
            """);

    public Task RemoveFromGroupsAsync(IReadOnlyList<string> vmNames)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $ids = @({{VmList(vmNames)}} | ForEach-Object { $_.Id })
            foreach ($group in @(Get-VMGroup)) {
                $members = @($group.VMMembers | Where-Object { $ids -contains $_.Id })
                if ($members.Count -eq 0) { continue }
                foreach ($vm in $members) { Remove-VMGroupMember -VMGroup $group -VM $vm -ErrorAction Stop }
                # A group these VMs leave empty is removed
                $left = Get-VMGroup -Name $group.Name -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($left -and -not $left.VMMembers -and -not $left.VMGroupMembers) { Remove-VMGroup -VMGroup $left -Force -ErrorAction SilentlyContinue }
            }
            """);

    // vmconnect window opened per VM: a second click brings it back instead of opening another one
    private static readonly Dictionary<string, Process> VmConnectWindows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Opens the Hyper-V VM Connect window (vmconnect.exe) for the specified VM, or brings the one HV-LAB
    /// already opened for it to the front. Equivalent to double-clicking the VM in Hyper-V Manager.
    /// </summary>
    public void ConnectToVMConsole(string name)
    {
        lock (VmConnectWindows)
        {
            if (VmConnectWindows.TryGetValue(name, out var open) && !open.HasExited)
            {
                open.Refresh();
                if (open.MainWindowHandle != 0)
                {
                    if (IsIconic(open.MainWindowHandle)) ShowWindow(open.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(open.MainWindowHandle);
                    return;
                }
            }

            var vmconnect = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "vmconnect.exe");

            if (!File.Exists(vmconnect))
                throw new InvalidOperationException(
                    LocalizationService.Instance["Err_VmConnect"]);

            // By GUID (-G) the window opens on that VM even when another one has the same name
            var psi = new ProcessStartInfo { FileName = vmconnect, UseShellExecute = false };
            psi.ArgumentList.Add("localhost");
            if (Guid.TryParse(name, out var id)) { psi.ArgumentList.Add("-G"); psi.ArgumentList.Add(id.ToString()); }
            else psi.ArgumentList.Add(name);
            if (Process.Start(psi) is { } process) VmConnectWindows[name] = process;
        }
    }

    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    // ─── Virtual Switches

    /// <summary>Switches through WMI: the Create VM and Quick VM pages load them on every visit.</summary>
    public Task<List<VirtualSwitch>> GetVirtualSwitchesAsync() => Wmi(_wmi.GetSwitches);

    public async Task CreateExternalSwitchAsync(string name, string netAdapter, int vlanId = 0)
    {
        var vlanScript = vlanId > 0
            ? $"Set-VMNetworkAdapterVlan -ManagementOS -VMNetworkAdapterName '{Esc(name)}' -Access -VlanId {vlanId} -ErrorAction Stop"
            : string.Empty;
        var script = $$"""
            Import-Module Hyper-V -ErrorAction Stop
            {{NoSwitchNamesake(name)}}
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
            {{NoSwitchNamesake(name)}}
            New-VMSwitch -Name '{{Esc(name)}}' -SwitchType Internal -ErrorAction Stop
            {{vlanScript}}
            """;
        await RunScriptAsync(script);
    }

    public async Task CreatePrivateSwitchAsync(string name)
        => await RunScriptAsync($"""
            {NoSwitchNamesake(name)}
            New-VMSwitch -Name '{Esc(name)}' -SwitchType Private -ErrorAction Stop
            """);

    public async Task RemoveVSwitchAsync(string switchIdOrName)
        => await RunScriptAsync(
            $"Remove-VMSwitch -VMSwitch {SwitchExpr(switchIdOrName)} -Force -ErrorAction Stop");

    // ─── Host network cards (vEthernet), one per VLAN ───────────────────────

    /// <summary>
    /// The Windows interface of the host card in $nic, found by device id (or its default alias), waited for a few
    /// seconds after the card is added.
    /// </summary>
    private const string HostNicInterface = """
        function Get-HvlNicInterface($card) {
            $deadline = (Get-Date).AddSeconds(15)
            do {
                $net = Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue |
                       Where-Object { $_.DeviceID -eq $card.DeviceId -or $_.Name -eq "vEthernet ($($card.Name))" } |
                       Select-Object -First 1
                if ($net) { return $net }
                Start-Sleep -Milliseconds 500
            } while ((Get-Date) -lt $deadline)
            throw '##Err_HostAdapterNoInterface'
        }
        """;

    /// <summary>PowerShell expression of the host card with this Hyper-V id.</summary>
    private static string HostAdapterExpr(string adapterId)
        => "$($hvlNic = @(Get-VMNetworkAdapter -ManagementOS | Where-Object Id -eq '" + Esc(adapterId) + "'); " +
           "if ($hvlNic.Count -ne 1) { throw '##Err_HostAdapterGone' }; $hvlNic[0])";

    private static void EnsureVlan(int vlanId)
    {
        if (vlanId is < 0 or > 4094) throw new ArgumentOutOfRangeException(nameof(vlanId), LocalizationService.T("HOSTNIC_ErrVlan"));
    }

    public async Task<List<HostAdapter>> GetHostAdaptersAsync()
    {
        var json = ExtractJson(await RunScriptAsync("""
            Import-Module Hyper-V -ErrorAction Stop
            $nets = @(Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue)
            $list = @(foreach ($nic in @(Get-VMNetworkAdapter -ManagementOS)) {
                $vlan = Get-VMNetworkAdapterVlan -VMNetworkAdapter $nic
                $net  = $nets | Where-Object { $_.DeviceID -eq $nic.DeviceId -or $_.Name -eq "vEthernet ($($nic.Name))" } | Select-Object -First 1
                $ip   = $null
                $dhcp = $true
                if ($net) {
                    $ip   = Get-NetIPAddress -InterfaceIndex $net.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
                    $dhcp = "$((Get-NetIPInterface -InterfaceIndex $net.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).Dhcp)" -eq 'Enabled'
                }
                [PSCustomObject]@{
                    Id = "$($nic.Id)"; Name = "$($nic.Name)"; SwitchName = "$($nic.SwitchName)"; SwitchId = "$($nic.SwitchId)".ToUpper()
                    VlanMode = "$($vlan.OperationMode)"; VlanId = [int]$vlan.AccessVlanId
                    Alias = if ($net) { "$($net.Name)" } else { '' }; Status = if ($net) { "$($net.Status)" } else { '' }
                    IPv4 = if ($ip) { "$($ip.IPAddress)" } else { '' }; PrefixLength = if ($ip) { [int]$ip.PrefixLength } else { 0 }
                    Dhcp = $dhcp
                }
            })
            ConvertTo-Json -InputObject $list -Depth 3 -Compress
            """));
        return ParseHostAdapters(json);
    }

    internal static List<HostAdapter> ParseHostAdapters(string json)
    {
        using var doc = JsonDocument.Parse(json.Length > 0 ? json : "[]");
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        static int I(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
        return rows.Select(r => new HostAdapter(
                       GetStr(r, "Id"), GetStr(r, "Name"), GetStr(r, "SwitchName"), GetStr(r, "SwitchId"), GetStr(r, "VlanMode"),
                       I(r, "VlanId"), GetStr(r, "Alias"), GetStr(r, "Status"), GetStr(r, "IPv4"), I(r, "PrefixLength"),
                       r.TryGetProperty("Dhcp", out var d) && d.ValueKind == JsonValueKind.True))
                   .OrderBy(a => a.SwitchName, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.VlanId).ThenBy(a => a.Name)
                   .ToList();
    }

    public Task AddHostAdapterAsync(string switchIdOrName, string name, int vlanId, bool dhcp)
    {
        if (InputValidator.CheckHostAdapterName(name) is { } key) throw new ArgumentException(LocalizationService.T(key), nameof(name));
        EnsureVlan(vlanId);
        return RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            {{HostNicInterface}}
            $sw   = {{SwitchExpr(switchIdOrName)}}
            $name = '{{Esc(name)}}'
            if ("$($sw.SwitchType)" -eq 'Private') { throw '##Err_HostAdapterPrivate' }
            if (@(Get-VMNetworkAdapter -ManagementOS | Where-Object Name -eq $name).Count -gt 0) { throw "##Err_HostAdapterExists|$name" }
            $nic = Add-VMNetworkAdapter -ManagementOS -Name $name -SwitchName $sw.Name -Passthru -ErrorAction Stop
            try {
                if ({{vlanId}} -gt 0) { Set-VMNetworkAdapterVlan -VMNetworkAdapter $nic -Access -VlanId {{vlanId}} -ErrorAction Stop }
                if (-not ${{(dhcp ? "true" : "false")}}) {
                    $net = Get-HvlNicInterface $nic
                    Set-NetIPInterface -InterfaceIndex $net.ifIndex -AddressFamily IPv4 -Dhcp Disabled -ErrorAction Stop
                }
            } catch {
                # A card half set up is removed: the host never keeps one on the wrong VLAN
                Remove-VMNetworkAdapter -VMNetworkAdapter $nic -Confirm:$false -ErrorAction SilentlyContinue
                throw
            }
            """);
    }

    public Task SetHostAdapterVlanAsync(string adapterId, int vlanId)
    {
        EnsureVlan(vlanId);
        return RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $nic = {{HostAdapterExpr(adapterId)}}
            if ({{vlanId}} -gt 0) { Set-VMNetworkAdapterVlan -VMNetworkAdapter $nic -Access -VlanId {{vlanId}} -ErrorAction Stop }
            else                  { Set-VMNetworkAdapterVlan -VMNetworkAdapter $nic -Untagged -ErrorAction Stop }
            """);
    }

    public Task SetHostAdapterDhcpAsync(string adapterId, bool enabled)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            {{HostNicInterface}}
            $nic = {{HostAdapterExpr(adapterId)}}
            $net = Get-HvlNicInterface $nic
            Set-NetIPInterface -InterfaceIndex $net.ifIndex -AddressFamily IPv4 -Dhcp {{(enabled ? "Enabled" : "Disabled")}} -ErrorAction Stop
            """);

    public Task RemoveHostAdapterAsync(string adapterId)
        => RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            Remove-VMNetworkAdapter -VMNetworkAdapter {{HostAdapterExpr(adapterId)}} -Confirm:$false -ErrorAction Stop
            """);

    // ─── Network Adapters and topology ──────────────────────────────────────

    /// <summary>Physical adapters that are up (WMI), for External switches and connection sharing.</summary>
    public Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync() => Wmi(_wmi.GetPhysicalAdapters);

    public Task<List<VmNetworkAdapter>> GetVmNetworkAdaptersAsync() => Wmi(_wmi.GetVmNetworkAdapters);

    public Task<Dictionary<string, string>> GetHostAddressesAsync() => Wmi(_wmi.GetHostAddresses);

    public Task ChangeSwitchTypeAsync(string switchIdOrName, string newType, string? netAdapter)
    {
        if (newType is not ("External" or "Internal" or "Private")) throw new ArgumentException(newType, nameof(newType));
        if (newType == "External" && string.IsNullOrWhiteSpace(netAdapter))
            throw new ArgumentException(LocalizationService.Instance["SW_ErrNoAdapter"]);
        return RunScriptAsync($$"""
            Import-Module Hyper-V -ErrorAction Stop
            $newType = '{{newType}}'
            $adapter = '{{Esc(netAdapter ?? "")}}'
            $sw      = {{SwitchExpr(switchIdOrName)}}
            $name    = $sw.Name
            $oldType = "$($sw.SwitchType)"
            $oldMgmt = [bool]$sw.AllowManagementOS
            $oldAdapter = if ($sw.NetAdapterInterfaceDescription) {
                (Get-NetAdapter -InterfaceDescription $sw.NetAdapterInterfaceDescription -ErrorAction SilentlyContinue).Name } else { '' }
            # Static IPv4 of the host adapter (a NAT gateway lives there): put back after the change or after a rollback
            $alias = "vEthernet ($name)"
            $ips   = @(Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                       Where-Object { "$($_.PrefixOrigin)" -eq 'Manual' })
            function Restore-HostIps {
                if (-not (Get-NetAdapter -Name $alias -ErrorAction SilentlyContinue)) { return }
                foreach ($ip in $ips) {
                    if (-not (Get-NetIPAddress -InterfaceAlias $alias -IPAddress $ip.IPAddress -ErrorAction SilentlyContinue)) {
                        New-NetIPAddress -InterfaceAlias $alias -IPAddress $ip.IPAddress -PrefixLength $ip.PrefixLength -ErrorAction SilentlyContinue | Out-Null
                    }
                }
            }
            try {
                if ($newType -eq 'External') { Set-VMSwitch -VMSwitch $sw -NetAdapterName $adapter -AllowManagementOS $true -ErrorAction Stop }
                else                         { Set-VMSwitch -VMSwitch $sw -SwitchType $newType -ErrorAction Stop }
            } catch {
                $failure = $_
                try {
                    if ($oldType -eq 'External' -and $oldAdapter) { Set-VMSwitch -VMSwitch $sw -NetAdapterName $oldAdapter -AllowManagementOS $oldMgmt -ErrorAction Stop }
                    elseif ($oldType -ne 'External')              { Set-VMSwitch -VMSwitch $sw -SwitchType $oldType -ErrorAction Stop }
                    Restore-HostIps
                } catch {}
                throw $failure
            }
            if ($newType -eq 'Internal') { Restore-HostIps }
            """);
    }

    // Internet connection sharing goes through the HNetCfg.HNetShare COM object (no cmdlet, no WMI class).
    // Sharing types: 0 = public (the connection that has Internet), 1 = private (the one that receives it).
    private const string IcsConnections = """
        $share = New-Object -ComObject HNetCfg.HNetShare
        $connections = @(foreach ($c in $share.EnumEveryConnection) {
            [PSCustomObject]@{
                Name   = $share.NetConnectionProps.Invoke($c).Name
                Config = $share.INetSharingConfigurationForINetConnection.Invoke($c)
            }
        })
        """;

    public async Task<IcsStatus?> GetIcsStatusAsync()
    {
        var json = ExtractJson(await RunScriptAsync(IcsConnections + """

            $public  = $connections | Where-Object { $_.Config.SharingEnabled -and $_.Config.SharingConnectionType -eq 0 } | Select-Object -First 1
            $private = $connections | Where-Object { $_.Config.SharingEnabled -and $_.Config.SharingConnectionType -eq 1 } | Select-Object -First 1
            [PSCustomObject]@{ Public = "$($public.Name)"; Private = "$($private.Name)" } | ConvertTo-Json -Compress
            """));
        try
        {
            using var doc = JsonDocument.Parse(json);
            var pub = GetStr(doc.RootElement, "Public");
            var priv = GetStr(doc.RootElement, "Private");
            return pub.Length > 0 && priv.Length > 0 ? new IcsStatus(pub, priv) : null;
        }
        catch (JsonException ex) { throw ParseError("ICS", json, ex); }
    }

    public Task EnableIcsAsync(string publicAdapter, string switchName)
        => RunScriptAsync(IcsConnections + $$"""

            $publicName  = '{{Esc(publicAdapter)}}'
            $privateName = 'vEthernet ({{Esc(switchName)}})'
            $public  = $connections | Where-Object Name -eq $publicName | Select-Object -First 1
            $private = $connections | Where-Object Name -eq $privateName | Select-Object -First 1
            # Names only ever come from single-quoted literals: never expanded inside a double-quoted string
            if (-not $public)  { throw "##Err_IcsNoConnection|$publicName" }
            if (-not $private) { throw "##Err_IcsNoConnection|$privateName" }
            # Windows allows a single sharing: turn the current one off first
            foreach ($c in $connections) { if ($c.Config.SharingEnabled) { $c.Config.DisableSharing() } }
            $public.Config.EnableSharing(0)
            $private.Config.EnableSharing(1)
            """);

    public Task DisableIcsAsync()
        => RunScriptAsync(IcsConnections + """

            foreach ($c in $connections) { if ($c.Config.SharingEnabled) { $c.Config.DisableSharing() } }
            """);

    // ─── Create blank VM (new empty VHDX) ───────────────────────────────────

    public async Task CreateBlankVmAsync(
        string vmName, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        long diskSizeGB, bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        InputValidator.EnsureVmName(vmName);
        var isGen2       = generation == 2 ? "$true" : "$false";
        var secureBootPs = secureBoot ? "$true" : "$false";
        var pxeBootPs    = pxeBoot    ? "$true" : "$false";

        var script = $$"""
            $vmName      = '{{Esc(vmName)}}'
            $notes       = {{NotesLiteral(notes)}}
            $switchName  = '{{Esc(switchName)}}'
            $memoryBytes = {{memoryMB}}MB
            $cpuCount    = {{cpuCount}}
            $generation  = {{generation}}
            $vmFolder    = '{{Esc(vmFolder)}}'
            $diskBytes   = {{diskSizeGB}}GB
            $isGen2      = {{isGen2}}
            $secureBoot  = {{secureBootPs}}
            $pxeBoot     = {{pxeBootPs}}

            # A namesake would share the folder and disk names, and later actions by name would hit both
            if (@(Get-VM | Where-Object Name -eq $vmName).Count -gt 0) { throw "##Err_VmExists|$vmName" }

            $vmPath    = Join-Path $vmFolder $vmName
            $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
            New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
            New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

            $vhdPath = Join-Path $vhdFolder "$vmName.vhdx"
            New-VHD -Path $vhdPath -SizeBytes $diskBytes -Dynamic -ErrorAction Stop | Out-Null

            $vm = New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                   -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop

            Set-VM -VM $vm -ProcessorCount $cpuCount -Notes $notes -ErrorAction Stop
            {{LabDefaults}}
            Add-VMHardDiskDrive -VM $vm -Path $vhdPath -ErrorAction Stop

            if ($generation -eq 2) {
                $sbState = if ($secureBoot) { 'On' } else { 'Off' }
                Set-VMFirmware -VM $vm -EnableSecureBoot $sbState `
                               -SecureBootTemplate 'MicrosoftWindows' -ErrorAction SilentlyContinue

                $hdd = Get-VMHardDiskDrive -VM $vm | Select-Object -First 1
                $nic = Get-VMNetworkAdapter -VM $vm | Select-Object -First 1

                if ($pxeBoot -and $nic -and $hdd) {
                    Set-VMFirmware -VM $vm -BootOrder $nic,$hdd -ErrorAction Stop
                } elseif ($hdd -and $nic) {
                    Set-VMFirmware -VM $vm -BootOrder $hdd,$nic -ErrorAction Stop
                } elseif ($hdd) {
                    Set-VMFirmware -VM $vm -FirstBootDevice $hdd -ErrorAction Stop
                } else {
                    throw "##Err_NoBootDisk|$vmName"
                }

                $first = (Get-VMFirmware -VM $vm).BootOrder | Select-Object -First 1
                if (-not $pxeBoot -and $first.BootType -eq 'Network') {
                    throw "##Err_BootOrder|$vmName"
                }
            } else {
                if ($pxeBoot) {
                    Set-VMBios -VM $vm -StartupOrder @('LegacyNetworkAdapter', 'IDE', 'CD', 'Floppy') -ErrorAction SilentlyContinue
                } else {
                    Set-VMBios -VM $vm -StartupOrder @('IDE', 'CD', 'LegacyNetworkAdapter', 'Floppy') -ErrorAction SilentlyContinue
                }
            }
            Write-Output "##Prg_VmCreated|$vmName"
            """;

        await RunCreationScriptAsync(script, vmName, vmFolder, progress, ct);
    }

    // ─── Create VM with differencing disk ───────────────────────────────────

    public async Task CreateVMWithDifferencingDiskAsync(
        string vmName, string parentVhdxPath, string switchName,
        long memoryMB, int cpuCount, int generation, string vmFolder,
        string? answerFileContent = null, bool secureBoot = true, bool pxeBoot = false, string? notes = null,
        string secureBootTemplate = BaseImageInfo.WindowsSecureBootTemplate, CloudInitSeed? cloudInit = null,
        IProgress<string>? progress = null, CancellationToken ct = default, string? staticMac = null)
    {
        InputValidator.EnsureVmName(vmName);
        // A Windows VM with a static IP: its adapter keeps this MAC, the interface identifier of unattend.xml
        var mac = staticMac is null ? "''" : $"'{Esc(staticMac.Replace("-", ""))}'";
        // Seed files as an ordered hashtable of literals (they hold the password: embedded like the answer file)
        var seedFiles = cloudInit is null ? "$null"
            : "[ordered]@{ " + string.Join("; ", cloudInit.Files().Select(f => $"'{Esc(f.Key)}' = {PsLiteral(f.Value)}")) + " }";
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
                $notes        = {{NotesLiteral(notes)}}
                $seedFiles    = {{seedFiles}}
                $sbTemplate   = '{{Esc(secureBootTemplate)}}'
                $isGen2       = {{isGen2}}
                $secureBoot   = {{secureBootPs}}
                $pxeBoot      = {{pxeBootPs}}
                $staticMac    = {{mac}}

                # A namesake would share the folder and disk names, and later actions by name would hit both
                if (@(Get-VM | Where-Object Name -eq $vmName).Count -gt 0) { throw "##Err_VmExists|$vmName" }

                $vmPath    = Join-Path $vmFolder $vmName
                $vhdFolder = Join-Path $vmPath 'Virtual Hard Disks'
                New-Item -Path $vmPath    -ItemType Directory -Force | Out-Null
                New-Item -Path $vhdFolder -ItemType Directory -Force | Out-Null

                $diffVhd = Join-Path $vhdFolder "$vmName.vhdx"
                New-VHD -Path $diffVhd -ParentPath $parentVhdx -Differencing -ErrorAction Stop | Out-Null

                # Inject unattend.xml into the differencing disk before first boot
                if ($answerXml) {
                    Write-Output "##Prg_Inject"
                    # -NoDriveLetter: the disk already has volumes; automatic letters would make Explorer pop up
                    $mount  = Mount-DiskImage -ImagePath $diffVhd -NoDriveLetter -PassThru -ErrorAction Stop
                    $diskNo = ($mount | Get-Disk).Number
                    # Use a temp folder path instead of a drive letter to avoid triggering Windows AutoPlay / Explorer
                    # Admin-only folder (not the user's %TEMP%): the answer file holds the admin password
                    $mountPath = Join-Path $env:ProgramData ('HV-LAB\mnt\' + [guid]::NewGuid().ToString('N'))
                    New-Item -Path $mountPath -ItemType Directory -Force | Out-Null
                    & icacls.exe $mountPath /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
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

                $vm = New-VM -Name $vmName -Path $vmFolder -MemoryStartupBytes $memoryBytes `
                       -Generation $generation -SwitchName $switchName -NoVHD -ErrorAction Stop

                Set-VM -VM $vm -ProcessorCount $cpuCount -Notes $notes -ErrorAction Stop
                {{LabDefaults}}
                if ($staticMac) {
                    Get-VMNetworkAdapter -VM $vm | Select-Object -First 1 | Set-VMNetworkAdapter -StaticMacAddress $staticMac -ErrorAction Stop
                }
                Add-VMHardDiskDrive -VM $vm -Path $diffVhd -ErrorAction Stop

                if ($generation -eq 2) {
                    # Secure Boot as chosen; a Linux clone cannot boot with the Windows template, so it must not fail silently
                    $sbState = if ($secureBoot) { 'On' } else { 'Off' }
                    $sbError = if ($sbTemplate -eq 'MicrosoftWindows') { 'SilentlyContinue' } else { 'Stop' }
                    Set-VMFirmware -VM $vm -EnableSecureBoot $sbState `
                                   -SecureBootTemplate $sbTemplate -ErrorAction $sbError

                    # Boot order strict: HDD first unless PXE explicitly requested
                    $hdd = Get-VMHardDiskDrive -VM $vm | Select-Object -First 1
                    $nic = Get-VMNetworkAdapter -VM $vm | Select-Object -First 1
                    if ($pxeBoot -and $nic -and $hdd) {
                        Set-VMFirmware -VM $vm -BootOrder $nic,$hdd -ErrorAction Stop
                    } elseif ($hdd -and $nic) {
                        Set-VMFirmware -VM $vm -BootOrder $hdd,$nic -ErrorAction Stop
                    } elseif ($hdd) {
                        Set-VMFirmware -VM $vm -FirstBootDevice $hdd -ErrorAction Stop
                    } else {
                        throw "##Err_NoBootDisk|$vmName"
                    }

                    $first = (Get-VMFirmware -VM $vm).BootOrder | Select-Object -First 1
                    if (-not $pxeBoot -and $first.BootType -eq 'Network') {
                        throw "##Err_BootOrder|$vmName"
                    }
                } else {
                    # Gen1 : BIOS
                    if ($pxeBoot) {
                        Set-VMBios -VM $vm -StartupOrder @('LegacyNetworkAdapter', 'IDE', 'CD', 'Floppy') -ErrorAction SilentlyContinue
                    } else {
                        Set-VMBios -VM $vm -StartupOrder @('IDE', 'CD', 'LegacyNetworkAdapter', 'Floppy') -ErrorAction SilentlyContinue
                    }
                }

                # Linux clone: cloud-init NoCloud seed on a second small disk, a FAT volume labelled CIDATA.
                # Same silent mount as the base images: no drive letter, admin-only folder under ProgramData.
                # Attached after the boot order is set, so the system disk stays first.
                if ($seedFiles) {
                    Write-Output "##Prg_Seed"
                    $seedVhd = Join-Path $vhdFolder "$vmName-cidata.vhdx"
                    New-VHD -Path $seedVhd -SizeBytes 64MB -Dynamic -ErrorAction Stop | Out-Null
                    $seedMount = Mount-DiskImage -ImagePath $seedVhd -NoDriveLetter -PassThru -ErrorAction Stop
                    $seedPath  = $null
                    try {
                        $seedDisk = ($seedMount | Get-Disk).Number
                        Initialize-Disk -Number $seedDisk -PartitionStyle MBR -ErrorAction Stop
                        $seedPart = New-Partition -DiskNumber $seedDisk -UseMaximumSize -ErrorAction Stop
                        Format-Volume -Partition $seedPart -FileSystem FAT -NewFileSystemLabel 'CIDATA' -Confirm:$false -ErrorAction Stop | Out-Null
                        $seedPath = Join-Path $env:ProgramData ('HV-LAB\mnt\' + [guid]::NewGuid().ToString('N'))
                        New-Item -Path $seedPath -ItemType Directory -Force | Out-Null
                        & icacls.exe $seedPath /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
                        $seedPart | Add-PartitionAccessPath -AccessPath $seedPath -ErrorAction Stop
                        # UTF-8 without BOM: cloud-init reads "#cloud-config" on the first line
                        $utf8 = New-Object System.Text.UTF8Encoding $false
                        foreach ($f in $seedFiles.GetEnumerator()) {
                            [System.IO.File]::WriteAllText((Join-Path $seedPath $f.Key), $f.Value, $utf8)
                        }
                    } finally {
                        if ($seedPath) {
                            try { $seedPart | Remove-PartitionAccessPath -AccessPath $seedPath -ErrorAction SilentlyContinue } catch {}
                            Remove-Item $seedPath -Force -ErrorAction SilentlyContinue
                        }
                        Dismount-DiskImage -ImagePath $seedVhd -ErrorAction SilentlyContinue | Out-Null
                    }
                    Add-VMHardDiskDrive -VM $vm -Path $seedVhd -ErrorAction Stop
                }
                Write-Output "##Prg_VmCreated|$vmName"
                """;
            await RunCreationScriptAsync(script, vmName, vmFolder, progress, ct);
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
