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
                Write-Error $_.Exception.Message
                exit 1
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
            if (Test-Path $vhdxPath)   { throw "Le fichier existe déjà : $vhdxPath" }
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
                    throw "Impossible d'obtenir une lettre pour le disque $diskNo partition $partNo"
                }

                Write-Output "##Prg_Mount"
                $mount  = Mount-DiskImage -ImagePath $vhdxPath -PassThru
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
                        $efiPart | Add-PartitionAccessPath -AssignDriveLetter
                        $efiLetter = Get-AssignedLetter $diskNo $efiPart.PartitionNumber

                        # MSR (16 MB, no letter)
                        New-Partition -DiskNumber $diskNo -Size 16MB `
                                      -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null

                        # Partition Windows
                        $winPart = New-Partition -DiskNumber $diskNo -UseMaximumSize
                        Format-Volume -Partition $winPart -FileSystem NTFS `
                                      -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                        $winPart | Add-PartitionAccessPath -AssignDriveLetter
                        $winLetter = Get-AssignedLetter $diskNo $winPart.PartitionNumber
                    } else {
                        Initialize-Disk -Number $diskNo -PartitionStyle MBR
                        $winPart = New-Partition -DiskNumber $diskNo -UseMaximumSize -IsActive
                        Format-Volume -Partition $winPart -FileSystem NTFS `
                                      -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                        $winPart | Add-PartitionAccessPath -AssignDriveLetter
                        $winLetter = Get-AssignedLetter $diskNo $winPart.PartitionNumber
                        $efiLetter = $null
                    }

                    $winPath = "${winLetter}:"

                    # Apply image via PowerShell cmdlet (more reliable than dism.exe /Apply-Image)
                    Write-Output "##Prg_Apply|$imageIndex"
                    Expand-WindowsImage -ImagePath $wimPath -Index $imageIndex `
                                        -ApplyPath "$winPath\" -ErrorAction Stop | Out-Null

                    Write-Output "##Prg_Bcd"
                    if ($isGen2) {
                        $bcd = & bcdboot.exe "$winPath\Windows" /s "${efiLetter}:" /f UEFI 2>&1
                    } else {
                        $bcd = & bcdboot.exe "$winPath\Windows" /s "$winPath" /f BIOS 2>&1
                    }
                    if ($LASTEXITCODE -ne 0) { throw "bcdboot a échoué : $bcd" }

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
                    Dismount-DiskImage -ImagePath $vhdxPath -ErrorAction SilentlyContinue | Out-Null
                }

                Write-Output "##Prg_Ready"
                """;
            await ps.RunAsync(script, progress, ct: ct);
        }
    }
}
