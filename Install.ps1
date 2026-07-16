#Requires -RunAsAdministrator
<#
.SYNOPSIS
	Installe HyperV Lab Manager sur la machine locale.

.DESCRIPTION
	Ce script :
	  1. Vérifie que Windows est en version Pro/Enterprise/Education (requis pour Hyper-V)
	  2. Installe le runtime .NET 8 Desktop si absent
	  3. Installe le Windows App SDK 1.6 Runtime si absent
	  4. Active Hyper-V si ce n'est pas déjà fait
	  5. Lance HVLab.exe (build framework-dependent)
		 OU installe le MSIX si présent dans le même dossier

.NOTES
	Doit être exécuté en tant qu'Administrateur.
	Testé sur Windows 10/11 Pro et Windows Server 2019/2022.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Couleurs ──────────────────────────────────────────────────────────────────
function Write-Step  ($msg) { Write-Host "  ► $msg" -ForegroundColor Cyan }
function Write-OK    ($msg) { Write-Host "  ✔ $msg" -ForegroundColor Green }
function Write-Warn  ($msg) { Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Fail  ($msg) { Write-Host "  ✖ $msg" -ForegroundColor Red }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║        HyperV Lab Manager — Installation         ║" -ForegroundColor Magenta
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

# ── 1. Trouver le .msix dans le même dossier que ce script ───────────────────
Write-Step "Recherche du package MSIX..."
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$msix = Get-ChildItem -Path $scriptDir -Filter "*.msix" -Recurse |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1

if (-not $msix) {
	Write-Fail "Aucun fichier .msix trouvé dans : $scriptDir"
	exit 1
}
Write-OK "Package trouvé : $($msix.Name)"

# ── 2. Vérifier l'édition Windows ────────────────────────────────────────────
Write-Step "Vérification de l'édition Windows..."
$os = Get-WmiObject Win32_OperatingSystem
$caption = $os.Caption

if ($caption -match "Home") {
	Write-Fail "Windows Home détecté : Hyper-V n'est pas disponible sur cette édition."
	Write-Fail "Utilisez Windows 10/11 Pro, Enterprise ou Education."
	exit 1
}
Write-OK "Édition compatible : $caption"

# ── 2. Installer .NET 8 Desktop Runtime si absent ────────────────────────────
Write-Step "Vérification du runtime .NET 8 Desktop..."
$dotnet8 = dotnet --list-runtimes 2>$null | Where-Object { $_ -match "Microsoft\.WindowsDesktop\.App 8\." }
if (-not $dotnet8) {
    Write-Warn ".NET 8 Desktop Runtime non trouvé — téléchargement..."
    $dotnetUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
    $dotnetInstaller = "$env:TEMP\dotnet8-desktop-runtime.exe"
    Invoke-WebRequest -Uri $dotnetUrl -OutFile $dotnetInstaller -UseBasicParsing
    Start-Process -FilePath $dotnetInstaller -ArgumentList "/quiet /norestart" -Wait
    Remove-Item $dotnetInstaller -Force
    Write-OK ".NET 8 Desktop Runtime installé."
} else {
    Write-OK ".NET 8 Desktop Runtime déjà présent : $($dotnet8 | Select-Object -First 1)"
}

# ── 3. Installer Windows App SDK 1.6 Runtime si absent ───────────────────────
Write-Step "Vérification du Windows App SDK 1.6 Runtime..."
$wasdk = Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.6" -ErrorAction SilentlyContinue
if (-not $wasdk) {
    Write-Warn "Windows App SDK 1.6 non trouvé — téléchargement..."
    $wasdkUrl = "https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe"
    $wasdkInstaller = "$env:TEMP\windowsappsdk-runtime.exe"
    Invoke-WebRequest -Uri $wasdkUrl -OutFile $wasdkInstaller -UseBasicParsing
    Start-Process -FilePath $wasdkInstaller -ArgumentList "--quiet" -Wait
    Remove-Item $wasdkInstaller -Force
    Write-OK "Windows App SDK 1.6 Runtime installé."
} else {
    Write-OK "Windows App SDK 1.6 Runtime déjà présent : $($wasdk.Version)"
}

# ── 4. Activer Hyper-V si absent ─────────────────────────────────────────────
Write-Step "Vérification de Hyper-V..."
$hyperv = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -ErrorAction SilentlyContinue

if ($hyperv -and $hyperv.State -ne "Enabled") {
	Write-Warn "Hyper-V non activé — activation en cours (redémarrage possible)..."
	Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All -NoRestart | Out-Null
	Write-OK "Hyper-V activé. Un redémarrage sera peut-être nécessaire."
} elseif ($hyperv) {
	Write-OK "Hyper-V déjà activé."
} else {
	Write-Warn "Impossible de vérifier Hyper-V (peut être un Windows Server avec rôle dédié)."
}

# ── 4. Installer le certificat de signature ───────────────────────────────────
Write-Step "Installation du certificat de signature..."

try {
	# Extraire le certificat directement depuis le .msix (ZIP)
	Add-Type -AssemblyName System.IO.Compression.FileSystem
	$zip  = [System.IO.Compression.ZipFile]::OpenRead($msix.FullName)
	$entry = $zip.Entries | Where-Object { $_.Name -eq "AppxSignature.p7x" } | Select-Object -First 1

	if ($entry) {
		$tmpSig = [System.IO.Path]::GetTempFileName() + ".p7x"
		[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $tmpSig, $true)
		$zip.Dispose()

		# Extraire le certificat depuis la signature
		$cms = New-Object System.Security.Cryptography.Pkcs.SignedCms
		$cms.Decode([System.IO.File]::ReadAllBytes($tmpSig))
		$cert = $cms.Certificates | Select-Object -First 1

		if ($cert) {
			$store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
				[System.Security.Cryptography.X509Certificates.StoreName]::Root,
				[System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
			$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

			# Vérifier si déjà présent
			$existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
			if (-not $existing) {
				$store.Add($cert)
				Write-OK "Certificat installé : $($cert.Subject) [$($cert.Thumbprint.Substring(0,12))...]"
			} else {
				Write-OK "Certificat déjà présent : $($cert.Subject)"
			}
			$store.Close()
		}
		Remove-Item $tmpSig -Force -ErrorAction SilentlyContinue
	} else {
		$zip.Dispose()
		Write-Warn "Signature introuvable dans le MSIX — installation du certificat ignorée."
	}
} catch {
	Write-Warn "Impossible d'extraire le certificat automatiquement : $_"
	Write-Warn "Installez manuellement le certificat : clic droit sur le .msix → Propriétés → Signatures numériques → Détails → Voir le certificat → Installer."
}

# ── 5. Installer le package MSIX ─────────────────────────────────────────────
Write-Step "Installation du package MSIX..."

try {
	# Désinstaller la version précédente si présente
	$existing = Get-AppxPackage -Name "HVLab.HyperVLabManager" -ErrorAction SilentlyContinue
	if ($existing) {
		Write-Warn "Version précédente détectée ($($existing.Version)) — suppression..."
		Remove-AppxPackage -Package $existing.PackageFullName -ErrorAction SilentlyContinue
	}

	Add-AppxPackage -Path $msix.FullName
	Write-OK "HyperV Lab Manager installé avec succès !"
} catch {
	Write-Fail "Échec de l'installation : $_"
	Write-Fail "Assurez-vous que le certificat est bien dans 'Autorités de certification racines de confiance'."
	exit 1
}

Write-Host ""
Write-Host "  Installation terminée !" -ForegroundColor Green
Write-Host "  Lancez 'HyperV Lab Manager' depuis le menu Démarrer." -ForegroundColor Green
Write-Host ""
