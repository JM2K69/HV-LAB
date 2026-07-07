using HVLab.Models;

namespace HVLab.Services;

public class VhdxService
{
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
