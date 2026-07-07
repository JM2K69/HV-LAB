using System.Text.Json;
using HVLab.Models;

namespace HVLab.Services;

public class VhdxService
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

        var json = await HyperVService.RunScriptAsync(script);
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // PowerShell may return a single object (not array) for 1-index WIMs
            json = json.Trim();
            if (json.StartsWith('{')) json = $"[{json}]";
            var raw = JsonSerializer.Deserialize<List<WimImageRaw>>(json, opts) ?? [];

            var result = raw.Select(r => new WimImageInfo
            {
                ImageIndex = r.ImageIndex,
                ImageName  = r.ImageName ?? string.Empty,
                Version    = r.Version   ?? string.Empty,
                ImageSize  = r.ImageSize,
            }).ToList();

            // If DISM didn't return a version, fall back to querying index 1 via WMI/DISM
            if (result.Count > 0 && string.IsNullOrWhiteSpace(result[0].Version))
            {
                var ver = await GetWimVersionAsync(wimPath);
                foreach (var img in result)
                    img.Version = ver;
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the Windows build version from WIM index 1 using DISM /Get-WimInfo.
    /// Parses the "Version :" line from the text output.
    /// </summary>
    private static async Task<string> GetWimVersionAsync(string wimPath)
    {
        var script = $$"""
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $out = & dism.exe /Get-WimInfo /WimFile:'{{Esc(wimPath)}}' /Index:1 2>&1
            $vline = $out | Where-Object { $_ -match '^\s*Version\s*:' } | Select-Object -First 1
            if ($vline) { ($vline -split ':',2)[1].Trim() } else { '' }
            """;
        try
        {
            var result = await HyperVService.RunScriptAsync(script);
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
            if (Test-Path $vhdxPath)   { Remove-Item $vhdxPath -Force }
            New-VHD -Path $vhdxPath -SizeBytes {{sizeGB}}GB -Dynamic -ErrorAction Stop | Out-Null
            Write-Output "VHDX créé : $vhdxPath"
            """;
        await HyperVService.RunScriptAsync(script);
    }

    public async Task ApplyWindowsImageAsync(
        string vhdxPath, string wimPath, int imageIndex, int generation,
        string? answerFileContent = null)
    {
        string? answerTemp = null;
        if (!string.IsNullOrWhiteSpace(answerFileContent))
        {
            answerTemp = Path.Combine(Path.GetTempPath(), $"hvlab_unattend_{Guid.NewGuid():N}.xml");
            await File.WriteAllTextAsync(answerTemp, answerFileContent, System.Text.Encoding.UTF8);
        }
        try
        {
            var isGen2   = generation == 2 ? "$true" : "$false";
            var script = $$"""
                $vhdxPath   = '{{Esc(vhdxPath)}}'
                $wimPath    = '{{Esc(wimPath)}}'
                $imageIndex = {{imageIndex}}
                $isGen2     = {{isGen2}}
                $answerFile = '{{Esc(answerTemp ?? "")}}'

                Write-Output "Montage du VHDX..."
                $mount  = Mount-DiskImage -ImagePath $vhdxPath -PassThru -ErrorAction Stop
                $diskNo = ($mount | Get-Disk).Number

                Write-Output "Partitionnement..."
                if ($isGen2) {
                    Initialize-Disk -Number $diskNo -PartitionStyle GPT -ErrorAction Stop
                    $efiPart = New-Partition -DiskNumber $diskNo -Size 100MB -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'
                    Format-Volume -Partition $efiPart -FileSystem FAT32 -NewFileSystemLabel 'System' -Confirm:$false | Out-Null
                    $efiLetter = ($efiPart | Add-PartitionAccessPath -AssignDriveLetter -PassThru).DriveLetter
                    New-Partition -DiskNumber $diskNo -Size 16MB -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null
                    $winPart   = New-Partition -DiskNumber $diskNo -UseMaximumSize
                    Format-Volume -Partition $winPart -FileSystem NTFS -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                    $winLetter = ($winPart | Add-PartitionAccessPath -AssignDriveLetter -PassThru).DriveLetter
                } else {
                    Initialize-Disk -Number $diskNo -PartitionStyle MBR -ErrorAction Stop
                    $winPart   = New-Partition -DiskNumber $diskNo -UseMaximumSize -IsActive
                    Format-Volume -Partition $winPart -FileSystem NTFS -NewFileSystemLabel 'Windows' -Confirm:$false | Out-Null
                    $winLetter = ($winPart | Add-PartitionAccessPath -AssignDriveLetter -PassThru).DriveLetter
                    $efiLetter = $null
                }

                $winPath = "$($winLetter):"
                Write-Output "Application de l'image Windows (quelques minutes)..."
                $dism = & dism.exe /Apply-Image /ImageFile:"$wimPath" /Index:$imageIndex /ApplyDir:"$winPath\" 2>&1
                if ($LASTEXITCODE -ne 0) { throw "DISM a échoué : $dism" }

                Write-Output "Configuration du démarrage..."
                if ($isGen2) {
                    & bcdboot.exe "$winPath\Windows" /s "$($efiLetter):" /f UEFI 2>&1 | Out-Null
                } else {
                    & bcdboot.exe "$winPath\Windows" /s "$winPath" /f BIOS 2>&1 | Out-Null
                }

                if ($answerFile -and (Test-Path $answerFile)) {
                    $panther = "$winPath\Windows\Panther"
                    if (-not (Test-Path $panther)) { New-Item -Path $panther -ItemType Directory -Force | Out-Null }
                    Copy-Item -Path $answerFile -Destination "$panther\unattend.xml" -Force
                    Write-Output "Fichier de réponse appliqué."
                }

                Write-Output "Démontage..."
                Dismount-DiskImage -ImagePath $vhdxPath | Out-Null
                Write-Output "Image prête : $vhdxPath"
                """;
            await HyperVService.RunScriptAsync(script);
        }
        finally
        {
            if (answerTemp is not null)
                try { File.Delete(answerTemp); } catch { /* ignore */ }
        }
    }

    private static string Esc(string s) => s.Replace("'", "''");
}
