using System.Text.Json;
using HVLab.Models;
using static HVLab.Services.PowerShellRunner;

namespace HVLab.Services;

public class VhdxService(IPowerShellRunner ps) : IVhdxService
{
    // ─── WIM / DISM analysis ─────────────────────────────────────────────────

    /// <summary>
    /// Lists all image indexes in a WIM/ESD file using Get-WindowsImage (DISM PowerShell module).
    /// Returns each entry as a <see cref="WimImageInfo"/>.
    /// </summary>
    public async Task<List<WimImageInfo>> GetWimImagesAsync(string wimPath)
    {
        if (string.IsNullOrWhiteSpace(wimPath) || !File.Exists(wimPath))
            return [];

        // Get-WindowsImage returns an array; we also pull the build version
        // via Get-WindowsImageContent which needs mounting — too slow.
        // Instead we grab Version from each ImageInfo object (available without mounting).
        var script = $$"""
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            try {
                $imgs = @(Get-WindowsImage -ImagePath '{{Esc(wimPath)}}' -ErrorAction Stop | ForEach-Object {
                    [PSCustomObject]@{
                        ImageIndex = $_.ImageIndex
                        ImageName  = $_.ImageName
                        Version    = if ($_.Version) { $_.Version } else { '' }
                        ImageSize  = $_.ImageSize
                    }
                })
                if ($imgs.Count -gt 0) { ConvertTo-Json -InputObject $imgs -Depth 2 } else { '[]' }
            } catch {
                throw   # rethrow: the runner reports the message
            }
            """;

        var json = await ps.RunAsync(script);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // PowerShell may return a single object (not array) for 1-index WIMs
            json = HyperVService.ExtractJson(json);
            if (json.StartsWith('{')) json = $"[{json}]";
            var raw = JsonSerializer.Deserialize<List<WimImageRaw>>(json, opts) ?? [];

            var result = raw.Select(r => new WimImageInfo
            {
                ImageIndex = r.ImageIndex,
                ImageName  = r.ImageName ?? string.Empty,
                Version    = r.Version   ?? string.Empty,
                ImageSize  = r.ImageSize,
            }).ToList();

            // Without -Index, Get-WindowsImage omits Version: query index 1 explicitly
            if (result.Count > 0 && string.IsNullOrWhiteSpace(result[0].Version))
            {
                var ver = await GetWimVersionAsync(wimPath);
                foreach (var img in result)
                    img.Version = ver;
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw HyperVService.ParseError("images WIM", json, ex);
        }
    }

    /// <summary>
    /// Reads the Windows build version of WIM index 1 with Get-WindowsImage -Index
    /// (structured property — independent of the OS display language, unlike dism.exe text).
    /// </summary>
    private async Task<string> GetWimVersionAsync(string wimPath)
    {
        var script = $$"""
            $img = Get-WindowsImage -ImagePath '{{Esc(wimPath)}}' -Index 1 -ErrorAction Stop
            if ($img.Version) { $img.Version } else { '' }
            """;
        try
        {
            var result = await ps.RunAsync(script);
            return result.Trim();
        }
        catch { return string.Empty; }
    }

    // JSON deserialization shim (matches PowerShell property names exactly)
    private sealed class WimImageRaw
    {
        public int    ImageIndex { get; set; }
        public string? ImageName { get; set; }
        public string? Version   { get; set; }
        public long   ImageSize  { get; set; }
    }


    public async Task<List<BaseVhdx>> GetBaseVhdxListAsync(string folder)
    {
        var result = new List<BaseVhdx>();
        if (!Directory.Exists(folder)) return result;
        var files = await Task.Run(() =>
            Directory.GetFiles(folder, "BASE_*.vhdx", SearchOption.TopDirectoryOnly));
        foreach (var f in files)
            result.Add(BaseVhdx.FromFile(f));
        // Sort by OS identifier then version descending
        result.Sort((a, b) => string.Compare(a.OsIdentifier + a.OsVersion,
                                              b.OsIdentifier + b.OsVersion,
                                              StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public async Task CreateBaseVhdxAsync(string vhdxPath, long sizeGB)
    {
        var script = $$"""
            $vhdxPath = '{{Esc(vhdxPath)}}'
            $dir = Split-Path $vhdxPath -Parent
            if (-not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
            if (Test-Path $vhdxPath)   { throw "##Err_FileExists|$vhdxPath" }
            New-VHD -Path $vhdxPath -SizeBytes {{sizeGB}}GB -Dynamic -ErrorAction Stop | Out-Null
            Write-Output "##BV_Created|$vhdxPath"
            """;
        await ps.RunAsync(script);
    }

    /// <summary>Dismounts (if needed) and deletes a partially built VHDX.</summary>
    public async Task DiscardPartialVhdxAsync(string vhdxPath)
    {
        var script = $$"""
            $vhdxPath = '{{Esc(vhdxPath)}}'
            Dismount-DiskImage -ImagePath $vhdxPath -ErrorAction SilentlyContinue | Out-Null
            if (Test-Path $vhdxPath) { Remove-Item $vhdxPath -Force }
            """;
        await ps.RunAsync(script);
    }

    public async Task EnsureDetachedAsync(string vhdxPath)
    {
        var script = $$"""
            $vhdxPath = '{{Esc(vhdxPath)}}'
            Dismount-DiskImage -ImagePath $vhdxPath -ErrorAction SilentlyContinue | Out-Null
            $deadline = (Get-Date).AddSeconds(30)
            while ((Get-Date) -lt $deadline) {
                $img = Get-DiskImage -ImagePath $vhdxPath -ErrorAction SilentlyContinue
                if (-not $img -or -not $img.Attached) { break }
                Start-Sleep -Milliseconds 500
            }
            """;
        await ps.RunAsync(script);
    }

    public async Task ApplyWindowsImageAsync(
        string vhdxPath, string wimPath, int imageIndex, int generation,
        string? answerFileContent = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Embedded in the script (streamed over stdin) — never written to %TEMP%.
        var answerXml = string.IsNullOrWhiteSpace(answerFileContent) ? "''" : HyperVService.PsLiteral(answerFileContent);
        {
            var isGen2  = generation == 2 ? "$true" : "$false";
            var script  = $$"""
                $ErrorActionPreference = 'Stop'
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

                $vhdxPath   = '{{Esc(vhdxPath)}}'
                $wimPath    = '{{Esc(wimPath)}}'
                $imageIndex = {{imageIndex}}
                $isGen2     = {{isGen2}}
                $answerXml  = {{answerXml}}

                # Helper : re-query the assigned drive letter (avoids the stale-object race condition)
                function Get-AssignedLetter([int]$diskNo, [int]$partNo) {
                    $deadline = (Get-Date).AddSeconds(15)
                    do {
                        $letter = (Get-Partition -DiskNumber $diskNo -PartitionNumber $partNo -ErrorAction SilentlyContinue).DriveLetter
                        if ($letter -and $letter -ne "`0") { return $letter }
                        Start-Sleep -Milliseconds 500
                    } while ((Get-Date) -lt $deadline)
                    throw "##Err_NoDriveLetter|$diskNo|$partNo"
                }

                # Temporary drive letter WITHOUT an Explorer window: mountvol does not trigger the shell
                # (Add-PartitionAccessPath -AssignDriveLetter and diskpart "assign" both open one)
                function Mount-SilentLetter($part) {
                    $vol = (Get-Partition -DiskNumber $part.DiskNumber -PartitionNumber $part.PartitionNumber).AccessPaths |
                           Where-Object { $_ -like '\\?\Volume*' } | Select-Object -First 1
                    $used = @((Get-PSDrive -PSProvider FileSystem).Name) + @(Get-CimInstance Win32_LogicalDisk | ForEach-Object { $_.DeviceID.TrimEnd(':') })
                    foreach ($c in [char[]](90..68)) {
                        $l = [string]$c
                        if ($used -contains $l -or (Test-Path "${l}:\")) { continue }
                        & mountvol.exe "${l}:" $vol | Out-Null
                        for ($i = 0; $i -lt 20; $i++) { if (Test-Path "${l}:\") { return "${l}:" }; Start-Sleep -Milliseconds 250 }
                        & mountvol.exe "${l}:" /D | Out-Null
                    }
                    throw "##Err_NoDriveLetter|$($part.DiskNumber)|$($part.PartitionNumber)"
                }

                Write-Output "##Prg_Mount"
                # -NoDriveLetter: no automatic letters, so Explorer does not pop up (letters are only
                # assigned explicitly, after formatting, where a tool needs one)
                $mount  = Mount-DiskImage -ImagePath $vhdxPath -NoDriveLetter -PassThru
                $diskNo = ($mount | Get-Disk).Number

                try {
                    Write-Output "##Prg_Partition|$diskNo"
                    if ($isGen2) {
                        Initialize-Disk -Number $diskNo -PartitionStyle GPT
                        # Partition EFI (100 MB FAT32)
                        $efiPart = New-Partition -DiskNumber $diskNo -Size 100MB `
                                       -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'
                        Format-Volume -Partition $efiPart -FileSystem FAT32 `
                                      -NewFileSystemLabel 'System' -Confirm:$false | Out-Null
                        # No drive letter on the ESP yet: it only gets one while the boot files are written

                        # MSR (16 MB, no letter) — Initialize-Disk GPT usually creates one already
                        $msrType = '{e3c9e316-0b5c-4db8-817d-f92df00215ae}'
                        if (-not (Get-Partition -DiskNumber $diskNo | Where-Object GptType -eq $msrType)) {
                            New-Partition -DiskNumber $diskNo -Size 16MB -GptType $msrType | Out-Null
                        }

                        # Partition Windows
                        $winPart = New-Partition -DiskNumber $diskNo -UseMaximumSize
                        Format-Volume -Partition $winPart -FileSystem NTFS `
                                      -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                    } else {
                        Initialize-Disk -Number $diskNo -PartitionStyle MBR
                        $winPart = New-Partition -DiskNumber $diskNo -UseMaximumSize -IsActive
                        Format-Volume -Partition $winPart -FileSystem NTFS `
                                      -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                        $efiPart = $null
                    }

                    # The Windows partition is mounted on an admin-only folder, never on a drive letter:
                    # a letter makes Explorer open a "Windows (X:)" window during the build
                    $winPath = Join-Path $env:ProgramData ('HV-LAB\mnt\' + [guid]::NewGuid().ToString('N'))
                    New-Item $winPath -ItemType Directory -Force | Out-Null
                    & icacls.exe $winPath /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
                    $winPart | Add-PartitionAccessPath -AccessPath $winPath

                    # Apply image via PowerShell cmdlet (more reliable than dism.exe /Apply-Image)
                    Write-Output "##Prg_Apply|$imageIndex"
                    Expand-WindowsImage -ImagePath $wimPath -Index $imageIndex `
                                        -ApplyPath "$winPath\" -ErrorAction Stop | Out-Null

                    Write-Output "##Prg_Bcd"
                    # ── Boot files of the OFFLINE image ───────────────────────────────────────────────────────
                    # 1. Manual (first): the image's own Windows\Boot files + a BCD built with bcdedit /store.
                    #    Deterministic, works on every host, and needs NO drive letter (folder mounts only),
                    #    so Explorer never pops up.
                    # 2. Fallback, bcdboot (needs a drive letter on the system partition, given with mountvol):
                    #    the image's bcdboot with its own bcd.dll + bcdsrv.dll, then host bcdboot /offline
                    #    /nofirmwaresync, then host bcdboot. On some hosts (Windows 11 Insider 26300) every bcdboot
                    #    fails with "Failed to create a new system store [c0000035]"; running bcdboot against a
                    #    lettered volume also makes Explorer open it.
                    # The host boot configuration is never touched: every call targets this VHDX (/store, /s).
                    if ($isGen2) {
                        $espPath = Join-Path $env:ProgramData ('HV-LAB\mnt\' + [guid]::NewGuid().ToString('N'))
                        New-Item $espPath -ItemType Directory -Force | Out-Null
                        & icacls.exe $espPath /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
                        $efiPart | Add-PartitionAccessPath -AccessPath $espPath
                        $sysPart  = $efiPart
                        $sysFiles = $espPath
                    } else {
                        $sysPart  = $winPart
                        $sysFiles = $winPath
                    }
                    $sysRoot = $null
                    $bcdFw   = if ($isGen2) { 'UEFI' } else { 'BIOS' }
                    $hostBcd = "$env:SystemRoot\System32\bcdboot.exe"
                    $bcdEdit = "$env:SystemRoot\System32\bcdedit.exe"
                    $imgSys  = "$winPath\Windows\System32"
                    $bcdDir  = "$winPath\HVLab-bcdboot"

                    # Best effort: a failed attempt can leave files behind (Gen1: TrustedInstaller-only ACLs)
                    function Clear-BootTarget {
                        if ($isGen2) { $paths = @("$sysFiles\EFI") }
                        else         { $paths = @("$sysFiles\Boot", "$sysFiles\bootmgr", "$sysFiles\BOOTNXT") }
                        foreach ($p in $paths) {
                            if (-not (Test-Path $p)) { continue }
                            if (-not $isGen2) {
                                & icacls.exe $p /setowner '*S-1-5-32-544' /T /C /Q 2>&1 | Out-Null
                                & icacls.exe $p /grant '*S-1-5-32-544:F' /T /C /Q 2>&1 | Out-Null
                                & attrib.exe -R -S -H $p /S /D 2>&1 | Out-Null
                            }
                            Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue
                        }
                    }

                    function Invoke-BcdEdit([string[]]$bcdArgs) {
                        $out = & $bcdEdit @bcdArgs 2>&1
                        if ($LASTEXITCODE -ne 0) { throw "bcdedit $($bcdArgs -join ' '): $out" }
                        return "$out"
                    }

                    # bcdedit /createstore makes an ordinary store, which Windows Setup refuses at first boot
                    # (specialize: "File is not system store", then "Windows could not configure Windows to run on
                    # this computer's hardware"). bcdboot marks its store with Description\System = 1; done here
                    # with the offline registry API, which edits the file without loading it as a hive.
                    function Set-SystemStoreFlag([string]$store) {
                        if (-not ('HvLab.OffReg' -as [type])) {
                            $sig = '[DllImport("offreg.dll", CharSet = CharSet.Unicode)] public static extern int OROpenHive(string path, out System.IntPtr hive);' +
                                   '[DllImport("offreg.dll", CharSet = CharSet.Unicode)] public static extern int OROpenKey(System.IntPtr key, string sub, out System.IntPtr result);' +
                                   '[DllImport("offreg.dll", CharSet = CharSet.Unicode)] public static extern int ORSetValue(System.IntPtr key, string name, int type, byte[] data, int size);' +
                                   '[DllImport("offreg.dll", CharSet = CharSet.Unicode)] public static extern int ORSaveHive(System.IntPtr hive, string path, int major, int minor);' +
                                   '[DllImport("offreg.dll")] public static extern int ORCloseKey(System.IntPtr key);' +
                                   '[DllImport("offreg.dll")] public static extern int ORCloseHive(System.IntPtr hive);'
                            Add-Type -Namespace HvLab -Name OffReg -MemberDefinition $sig
                        }
                        $hive = [IntPtr]::Zero; $key = [IntPtr]::Zero
                        $rc = [HvLab.OffReg]::OROpenHive($store, [ref]$hive)
                        if ($rc -ne 0) { throw "offreg: cannot open $store ($rc)" }
                        try {
                            $rc = [HvLab.OffReg]::OROpenKey($hive, 'Description', [ref]$key)
                            if ($rc -ne 0) { throw "offreg: no Description key in $store ($rc)" }
                            $rc = [HvLab.OffReg]::ORSetValue($key, 'System', 4, [BitConverter]::GetBytes([int]1), 4)
                            [void][HvLab.OffReg]::ORCloseKey($key)
                            if ($rc -ne 0) { throw "offreg: cannot set System in $store ($rc)" }
                            # ORSaveHive cannot overwrite the open file: save next to it, then swap
                            $rc = [HvLab.OffReg]::ORSaveHive($hive, "$store.new", 6, 1)
                            if ($rc -ne 0) { throw "offreg: cannot save $store ($rc)" }
                        } finally { [void][HvLab.OffReg]::ORCloseHive($hive) }
                        Remove-Item $store -Force
                        Get-ChildItem "$store.LOG*" -Force -ErrorAction SilentlyContinue | Remove-Item -Force
                        Move-Item "$store.new" $store
                    }

                    # Same result as bcdboot: boot manager + boot files on the system partition, BCD with one
                    # Windows loader. Devices are 'boot' / 'locate' (as Microsoft's Convert-WindowsImage does),
                    # never "partition=": for a volume of a mounted VHDX, bcdedit records it as a partition
                    # INSIDE that .vhdx file on the host, which the VM cannot open (winload 0xc000000e).
                    function New-ManualBootFiles {
                        $bootSrc = "$winPath\Windows\Boot"
                        if ($isGen2) {
                            if (-not (Test-Path "$bootSrc\EFI\bootmgfw.efi")) { throw "missing $bootSrc\EFI\bootmgfw.efi" }
                            $mgrDir = "$sysFiles\EFI\Microsoft\Boot"
                            New-Item $mgrDir -ItemType Directory -Force | Out-Null
                            Copy-Item "$bootSrc\EFI\*" $mgrDir -Recurse -Force
                            New-Item "$sysFiles\EFI\Boot" -ItemType Directory -Force | Out-Null
                            Copy-Item "$mgrDir\bootmgfw.efi" "$sysFiles\EFI\Boot\bootx64.efi" -Force
                            $loader = '\Windows\system32\winload.efi'
                        } else {
                            if (-not (Test-Path "$bootSrc\PCAT\bootmgr")) { throw "missing $bootSrc\PCAT\bootmgr" }
                            $mgrDir = "$sysFiles\Boot"
                            New-Item $mgrDir -ItemType Directory -Force | Out-Null
                            Copy-Item "$bootSrc\PCAT\*" $mgrDir -Recurse -Force
                            Copy-Item "$bootSrc\PCAT\bootmgr" "$sysFiles\bootmgr" -Force
                            $loader = '\Windows\system32\winload.exe'
                        }
                        foreach ($d in 'Fonts', 'Resources') {
                            if (Test-Path "$bootSrc\$d") { Copy-Item "$bootSrc\$d" $mgrDir -Recurse -Force }
                        }
                        $store = "$mgrDir\BCD"
                        Invoke-BcdEdit @('/createstore', $store) | Out-Null
                        Invoke-BcdEdit @('/store', $store, '/create', '{bootmgr}', '/d', 'Windows Boot Manager') | Out-Null
                        Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'device', 'boot') | Out-Null
                        if ($isGen2) { Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'path', '\EFI\Microsoft\Boot\bootmgfw.efi') | Out-Null }
                        Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'locale', 'en-US') | Out-Null
                        Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'timeout', '30') | Out-Null
                        $created = Invoke-BcdEdit @('/store', $store, '/create', '/d', 'Windows', '/application', 'osloader')
                        $os = ([regex]'\{[0-9a-fA-F-]{36}\}').Match($created).Value
                        if (-not $os) { throw "bcdedit: no loader id in '$created'" }
                        foreach ($kv in @(@('device', 'locate'), @('osdevice', 'locate'),
                                          @('path', $loader), @('systemroot', '\Windows'), @('locale', 'en-US'),
                                          @('bootmenupolicy', 'Standard'))) {
                            Invoke-BcdEdit @('/store', $store, '/set', $os, $kv[0], $kv[1]) | Out-Null
                        }
                        Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'default', $os) | Out-Null
                        Invoke-BcdEdit @('/store', $store, '/set', '{bootmgr}', 'displayorder', $os) | Out-Null
                        Set-SystemStoreFlag $store
                    }

                    $bcdOk = $false; $bcdErrors = @()
                    $ErrorActionPreference = 'Continue'   # native stderr must not abort before the fallback
                    try {
                        try   { New-ManualBootFiles; $bcdOk = $true }
                        catch { $bcdErrors += "manual bcdedit: $($_.Exception.Message)" }

                        if (-not $bcdOk) {
                            $bcdTries = @()
                            if (Test-Path "$imgSys\bcdboot.exe") {
                                New-Item $bcdDir -ItemType Directory -Force | Out-Null
                                # Administrators + SYSTEM only: these binaries run elevated
                                & icacls.exe $bcdDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' | Out-Null
                                foreach ($f in 'bcdboot.exe', 'bcd.dll', 'bcdsrv.dll') {
                                    if (Test-Path "$imgSys\$f") { Copy-Item "$imgSys\$f" $bcdDir -Force }
                                }
                                $bcdTries += @{ Tool = "$bcdDir\bcdboot.exe"; Extra = @(); Label = 'image bcdboot' }
                            }
                            $bcdTries += @{ Tool = $hostBcd; Extra = @('/offline', '/nofirmwaresync'); Label = 'host bcdboot /offline' }
                            $bcdTries += @{ Tool = $hostBcd; Extra = @();                               Label = 'host bcdboot' }
                            $sysRoot = Mount-SilentLetter $sysPart
                            foreach ($bcdTry in $bcdTries) {
                                Clear-BootTarget
                                $bcdArgs = @("$winPath\Windows", '/s', $sysRoot, '/f', $bcdFw) + $bcdTry.Extra
                                $bcd = & $bcdTry.Tool @bcdArgs 2>&1
                                if ($LASTEXITCODE -eq 0) { $bcdOk = $true; break }
                                $bcdErrors += "$($bcdTry.Label): $bcd"
                            }
                        }
                    } finally {
                        if (Test-Path $bcdDir) { Remove-Item $bcdDir -Recurse -Force -ErrorAction SilentlyContinue }
                        if ($sysRoot) { & mountvol.exe $sysRoot /D | Out-Null }
                        if ($isGen2 -and $espPath) {
                            try { $efiPart | Remove-PartitionAccessPath -AccessPath $espPath -ErrorAction SilentlyContinue } catch {}
                            Remove-Item $espPath -Force -ErrorAction SilentlyContinue
                        }
                    }
                    $ErrorActionPreference = 'Stop'
                    if (-not $bcdOk) { throw "##Err_Bcdboot|$($bcdErrors -join ' / ')" }

                    if ($answerXml) {
                        $panther = "$winPath\Windows\Panther"
                        if (-not (Test-Path $panther)) {
                            New-Item -Path $panther -ItemType Directory -Force | Out-Null
                        }
                        [System.IO.File]::WriteAllText("$panther\unattend.xml", $answerXml, [System.Text.Encoding]::UTF8)
                        Write-Output "##Prg_Answer"
                    }
                }
                finally {
                    Write-Output "##Prg_Dismount"
                    if ($winPath -and (Test-Path $winPath)) {
                        try { $winPart | Remove-PartitionAccessPath -AccessPath $winPath -ErrorAction SilentlyContinue } catch {}
                        Remove-Item $winPath -Force -ErrorAction SilentlyContinue
                    }
                    Dismount-DiskImage -ImagePath $vhdxPath -ErrorAction SilentlyContinue | Out-Null
                }

                Write-Output "##Prg_Ready"
                """;
            await ps.RunAsync(script, progress, ct: ct);
        }
    }
}
