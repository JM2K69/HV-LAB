#!/usr/bin/env pwsh

# HV-LAB Quick Start
# Démarrage rapide de l'application

Write-Host "╔═════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  HV-LAB Manager - Démarrage Rapide    ║" -ForegroundColor Cyan
Write-Host "╚═════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Vérifier Hyper-V
Write-Host "🔍 Vérification de Hyper-V..." -ForegroundColor Yellow
$hvFeature = Get-WindowsOptionalFeature -FeatureName Hyper-V -Online -ErrorAction SilentlyContinue

if ($null -eq $hvFeature -or $hvFeature.State -ne "Enabled") {
    Write-Host "❌ Hyper-V n'est pas activé" -ForegroundColor Red
    Write-Host "   Pour activer Hyper-V, exécutez:" -ForegroundColor Gray
    Write-Host "   Enable-WindowsOptionalFeature -FeatureName Hyper-V -Online -All" -ForegroundColor Gray
    exit 1
}
Write-Host "✅ Hyper-V activé" -ForegroundColor Green
Write-Host ""

# Vérifier administrateur
Write-Host "🔍 Vérification des droits..." -ForegroundColor Yellow
if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "❌ Vous devez exécuter ce script en tant qu'administrateur" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Droits administrateur confirmés" -ForegroundColor Green
Write-Host ""

# Aller au répertoire du projet
Write-Host "📁 Accès au répertoire du projet..." -ForegroundColor Yellow
$projectPath = Join-Path $PSScriptRoot "HVLab"
if (-not (Test-Path $projectPath)) {
    Write-Host "❌ Répertoire du projet non trouvé: $projectPath" -ForegroundColor Red
    exit 1
}
cd $projectPath
Write-Host "✅ Répertoire du projet: $((Get-Location).Path)" -ForegroundColor Green
Write-Host ""

# Compiler
Write-Host "🔨 Compilation du projet..." -ForegroundColor Yellow
dotnet build --configuration Release --quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Erreur lors de la compilation" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Compilation réussie" -ForegroundColor Green
Write-Host ""

# Lancer l'application
Write-Host "🚀 Lancement de HV-LAB Manager..." -ForegroundColor Yellow
Write-Host "   Application en cours de démarrage..." -ForegroundColor Gray
Write-Host ""

dotnet run --configuration Release --no-build

Write-Host ""
Write-Host "👋 Merci d'avoir utilisé HV-LAB Manager!" -ForegroundColor Cyan
