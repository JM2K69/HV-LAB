#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installe (ou désinstalle) HV-LAB Manager sur la machine locale.

.DESCRIPTION
    L'application est publiée en dossier autonome (.NET et Windows App SDK inclus) :
    aucun runtime à télécharger. Ce script :
      1. copie le dossier publié vers C:\Program Files\HV-LAB (dossier modifiable par les seuls administrateurs) ;
      2. crée un raccourci dans le menu Démarrer (et sur le Bureau avec -DesktopShortcut) ;
      3. vérifie la présence de Hyper-V (et l'active avec -EnableHyperV).

    Publier l'application avant l'installation :
        dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release

.PARAMETER Source
    Dossier contenant HVLab.exe. Par défaut : HVLab\publish à côté de ce script,
    sinon le dossier du script lui-même (cas d'une archive de distribution).

.PARAMETER Destination
    Dossier d'installation. Par défaut : C:\Program Files\HV-LAB.

.PARAMETER DesktopShortcut
    Crée aussi un raccourci sur le Bureau public.

.PARAMETER EnableHyperV
    Active la fonctionnalité Hyper-V si elle est absente (redémarrage requis).

.PARAMETER Uninstall
    Supprime le dossier d'installation et les raccourcis.

.EXAMPLE
    .\Install.ps1
.EXAMPLE
    .\Install.ps1 -DesktopShortcut -EnableHyperV
.EXAMPLE
    .\Install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$Destination = (Join-Path $env:ProgramFiles 'HV-LAB'),
    [switch]$DesktopShortcut,
    [switch]$EnableHyperV,
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step ($msg) { Write-Host "  > $msg" -ForegroundColor Cyan }
function Write-OK   ($msg) { Write-Host "  OK $msg" -ForegroundColor Green }
function Write-Warn ($msg) { Write-Host "  !  $msg" -ForegroundColor Yellow }
function Write-Fail ($msg) { Write-Host "  X  $msg" -ForegroundColor Red }

$AppName       = 'HV-LAB Manager'
$ExeName       = 'HVLab.exe'
$StartMenuLink = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
$DesktopLink   = Join-Path $env:PUBLIC "Desktop\$AppName.lnk"

function Stop-RunningApp {
    $running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue |
               Where-Object { $_.Path -and $_.Path.StartsWith($Destination, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        Write-Warn "$AppName est en cours d'exécution : fermeture..."
        $running | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
}

# True when the shortcut targets an exe inside $Destination (so -Uninstall of one copy
# never removes the shortcut of another installation)
function Test-ShortcutOwned ([string]$Path) {
    if (-not (Test-Path $Path)) { return $false }
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($Path).TargetPath
    return $target -and $target.StartsWith($Destination.TrimEnd('') + '', [StringComparison]::OrdinalIgnoreCase)
}

function New-Shortcut ([string]$Path, [string]$Target) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath       = $Target
    $lnk.WorkingDirectory = Split-Path $Target -Parent
    $lnk.IconLocation     = "$Target,0"
    $lnk.Description      = 'Gestionnaire Hyper-V — VM, vSwitch, NAT, VHDX'
    $lnk.Save()
}

Write-Host ''
Write-Host "  === $AppName — $(if ($Uninstall) { 'Désinstallation' } else { 'Installation' }) ===" -ForegroundColor Magenta
Write-Host ''

# ── Désinstallation ──────────────────────────────────────────────────────────
if ($Uninstall) {
    Stop-RunningApp
    foreach ($link in $StartMenuLink, $DesktopLink) {
        if (Test-ShortcutOwned $link) { Remove-Item $link -Force; Write-OK "Raccourci supprimé : $link" }
        elseif (Test-Path $link)       { Write-Warn "Raccourci conservé (il pointe vers une autre installation) : $link" }
    }
    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
        Write-OK "Dossier supprimé : $Destination"
    } else {
        Write-Warn "Aucune installation trouvée dans $Destination"
    }
    Write-Host ''
    Write-Host '  Les paramètres utilisateur (%LocalAppData%\HV-LAB) sont conservés.' -ForegroundColor Gray
    exit 0
}

# ── 1. Dossier source ────────────────────────────────────────────────────────
Write-Step 'Recherche des fichiers de l''application...'
$candidates = if ($Source) { @($Source) } else { @((Join-Path $PSScriptRoot 'HVLab\publish'), $PSScriptRoot) }
$Source = $candidates | Where-Object { Test-Path (Join-Path $_ $ExeName) } | Select-Object -First 1
if (-not $Source) {
    Write-Fail "$ExeName introuvable dans : $($candidates -join ', ')"
    Write-Fail 'Publiez d''abord l''application : dotnet publish HVLab\HVLab.csproj -p:PublishProfile=HVLab-Release'
    exit 1
}
$Source = (Resolve-Path $Source).Path
if ($Source.TrimEnd('\') -ieq $Destination.TrimEnd('\')) {
    Write-Fail 'La source et la destination sont identiques.'
    exit 1
}
Write-OK "Source : $Source"

# ── 2. Hyper-V ───────────────────────────────────────────────────────────────
Write-Step 'Vérification de Hyper-V...'
if (Get-Module -ListAvailable -Name Hyper-V) {
    Write-OK 'Module PowerShell Hyper-V présent.'
} else {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -ErrorAction SilentlyContinue
    if ($EnableHyperV -and $feature) {
        Write-Warn 'Activation de Hyper-V...'
        Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All -NoRestart | Out-Null
        Write-OK 'Hyper-V activé : redémarrez la machine avant d''utiliser l''application.'
    } elseif ($feature) {
        Write-Warn 'Hyper-V n''est pas activé. Relancez avec -EnableHyperV ou activez-le manuellement.'
    } else {
        Write-Warn 'Hyper-V n''est pas disponible sur cette édition de Windows (Pro, Enterprise, Education ou Server requis).'
    }
}

# ── 3. Ancienne installation MSIX ────────────────────────────────────────────
$legacy = Get-AppxPackage -Name 'HVLab.HyperVLabManager' -AllUsers -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Warn "Ancienne version MSIX détectée ($($legacy.Version)) : suppression..."
    $legacy | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
}

# ── 4. Copie ─────────────────────────────────────────────────────────────────
Write-Step "Copie vers $Destination..."
Stop-RunningApp
New-Item -Path $Destination -ItemType Directory -Force | Out-Null
# /MIR : miroir exact (supprime les fichiers d'une version précédente qui n'existent plus)
& robocopy.exe $Source $Destination /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Fail "La copie a échoué (robocopy code $LASTEXITCODE)."
    exit 1
}
$global:LASTEXITCODE = 0
# Files extracted from a downloaded release zip carry the "from the Internet" mark: clear it
Get-ChildItem $Destination -Recurse -File | Unblock-File
$exe = Join-Path $Destination $ExeName
Write-OK "$((Get-ChildItem $Destination -Recurse -File).Count) fichiers installés."

# ── 5. Raccourcis ────────────────────────────────────────────────────────────
Write-Step 'Création des raccourcis...'
New-Shortcut -Path $StartMenuLink -Target $exe
Write-OK "Menu Démarrer : $AppName"
if ($DesktopShortcut) {
    New-Shortcut -Path $DesktopLink -Target $exe
    Write-OK "Bureau : $AppName"
}

Write-Host ''
Write-Host "  Installation terminée. Lancez « $AppName » depuis le menu Démarrer." -ForegroundColor Green
Write-Host '  (L''application demande les droits administrateur au démarrage.)' -ForegroundColor Gray
Write-Host ''
