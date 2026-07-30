# HyperV Lab Manager

> 🇫🇷 [Version française disponible ici](README.fr.md)

A modern **WinUI 3 / .NET 8** desktop application for managing Microsoft Hyper-V lab environments on Windows.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![WinUI](https://img.shields.io/badge/UI-WinUI%203-blueviolet)
![License](https://img.shields.io/badge/license-MIT-green)

---

## Features

- ✅ **Virtual Machine Management** — Create, start, stop, delete and connect to VM consoles (Gen 1/2, Secure Boot, boot order)
- ✅ **Differencing Disk Deployment** — Deploy VMs instantly from base VHDX images using differencing disks
- ✅ **Automatic Unattend.xml** — Generate answer files compatible with Windows Server and Client
- ✅ **KMS Key Injection** — Automatic GVLK key selection and injection based on the selected OS
- ✅ **Virtual Switch Management** — Create External, Internal and Private switches with optional VLAN support
- ✅ **NAT Networking** — Configure NAT networks with custom gateway and prefix
- ✅ **VM Console** — Connect directly to VM console via `vmconnect` (like Hyper-V Manager)
- ✅ **Base VHDX Image Management** — Register and manage base images for lab deployments
- ✅ **Multilingual UI** — Full French 🇫🇷 and English 🇬🇧 interface
- ✅ **Modern UI** — WinUI 3 with Mica backdrop, light/dark theme support

---

## Requirements

- **Windows 10 Pro/Enterprise** or **Windows 11 Pro/Enterprise** (Hyper-V enabled)
- **.NET 8.0 Runtime** or later
- **PowerShell 5.1+** (included in Windows)
- **Hyper-V** enabled and accessible
- Administrator privileges (required for Hyper-V operations)

---

## Quick Start

```bash
# Clone the repository
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB/HVLab

# Build
dotnet build -c Release

# Run
dotnet run -c Release
```

> ⚠️ The application must be run as **Administrator** to interact with Hyper-V.

---

## Project Structure

```
HVLab/
├── Models/                     # Data models
│   ├── VirtualMachine.cs       # VM model (ObservableObject)
│   ├── VirtualSwitch.cs        # Virtual switch model
│   ├── BaseVhdx.cs             # Base VHDX image model
│   └── NatNetwork.cs           # NAT network model
├── Services/                   # Business logic
│   ├── HyperVService.cs        # Hyper-V PowerShell integration
│   ├── NatService.cs           # NAT network management
│   ├── LocalizationService.cs  # FR/EN localization
│   └── AppSettings.cs          # Persistent settings
├── ViewModels/                 # MVVM ViewModels
│   ├── VirtualMachinesViewModel.cs
│   ├── VirtualSwitchesViewModel.cs
│   ├── BaseVhdxViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                      # XAML UI pages
│   ├── VirtualMachinesPage.xaml
│   ├── VirtualSwitchesPage.xaml
│   ├── BaseVhdxPage.xaml
│   ├── CreateVmPage.xaml
│   └── SettingsPage.xaml
├── Images/                     # App icons and assets
├── MainWindow.xaml             # Main window with navigation
└── App.xaml                    # Application bootstrap
```

---

## Usage

### 1. Manage Virtual Machines

1. Navigate to **Virtual Machines**
2. Click **Refresh** to load the current VM list
3. Use the action buttons to **Start**, **Stop**, **Connect** or **Delete** a VM
4. The list shows: name, state, generation, RAM, CPU, switch and VLAN info

### 2. Create a Virtual Machine

1. Navigate to **Create a VM**
2. Fill in the parameters:
   - VM name and administrator password
   - Generation (Gen 1 or Gen 2) — Gen 2 enables Secure Boot automatically
   - RAM, CPU count
   - Select a base VHDX image
   - Select a virtual switch
   - KMS key is auto-selected based on the chosen OS
3. Click **Create** — a differencing disk is created and `unattend.xml` is injected automatically

### 3. Manage Virtual Switches

1. Navigate to **Switch & NAT → Switches** tab
2. Click **Create** and fill in:
   - Switch name
   - Type: External (requires a network adapter), Internal or Private
   - Optional VLAN ID
3. To delete a switch, use the **Delete** button on the switch row

### 4. Configure NAT

1. Navigate to **Switch & NAT → NAT** tab
2. Fill in:
   - NAT name
   - Select an Internal switch
   - Gateway IP and prefix length
3. Click **Create NAT**

### 5. Manage Base VHDX Images

1. Navigate to **Base VHDX Images**
2. Register base images (WIM/VHDX) that will be used as parents for differencing disks
3. Images are tagged with OS identifier for automatic KMS key selection

### 6. Settings

- Choose the **interface language** (FR / EN)
- Set **default folders** for VMs and base images
- View app info and author in the **About** section

---

## PowerShell Integration

The application wraps the following Hyper-V PowerShell cmdlets:

| Operation | PowerShell Cmdlet |
|-----------|-------------------|
| List VMs | `Get-VM` |
| Create VM | `New-VM` |
| Start VM | `Start-VM` |
| Stop VM | `Stop-VM -Force` |
| Delete VM | `Remove-VM -Force` |
| List switches | `Get-VMSwitch` |
| Create switch | `New-VMSwitch` |
| Set VLAN | `Set-VMNetworkAdapterVlan` |
| Create differencing disk | `New-VHD -Differencing` |
| Apply WIM image | `DISM` via `Convert-WindowsImage` |
| VM console | `vmconnect.exe` |

---

## NuGet Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.WindowsAppSDK` | 1.6+ | WinUI 3 framework |
| `Microsoft.Windows.SDK.BuildTools` | latest | Build tools |
| `CommunityToolkit.Mvvm` | 8.2.2 | MVVM with source generators |
| `CommunityToolkit.WinUI.Controls.SettingsControls` | latest | Settings UI controls |

---

## Troubleshooting

### Application won't start

```
❌ "Windows App SDK not found"
✅ Run: dotnet restore && dotnet build -c Release

❌ "Hyper-V not available"
✅ Enable Hyper-V in Windows Features (optionalfeatures.exe)
```

### No VMs or switches displayed

```
❌ Empty list after Refresh
✅ Check that:
   - Hyper-V is enabled and running
   - The app is running as Administrator
   - PowerShell can execute: Get-VM / Get-VMSwitch
```

### Cannot create a VM

```
❌ "Access Denied"
✅ Run the application as Administrator
```

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## Author & Support

- **Author**: [JM2K69](https://github.com/JM2K69)
- **Issues**: [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/JM2K69/HV-LAB/discussions)

---

© 2026 JM2K69 — All rights reserved

## Prérequis

- **Windows 10 Pro/Enterprise** ou **Windows 11 Pro/Enterprise** (avec Hyper-V activé)
- **.NET 8.0 Runtime** ou plus récent
- **PowerShell 5.1+** (inclus dans Windows)
- **Hyper-V** activé et configurable

## Installation rapide

```bash
# Cloner le repo
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB/HVLab

# Compiler l'application
dotnet build -c Release

# Lancer l'application
dotnet run -c Release
```

## Architecture

### Structure du Projet

```
HVLab/
├── Models/                 # Modèles de données
│   ├── VirtualSwitch.cs   # Commutateurs virtuels
│   ├── VirtualMachine.cs  # Machines virtuelles
│   └── BaseVhdx.cs        # Images VHDX
├── Services/              # Couche métier
│   └── HyperVService.cs   # Service Hyper-V avec intégration PowerShell
├── ViewModels/            # ViewModels MVVM
│   └── MainViewModel.cs   # ViewModel principal avec RelayCommands
├── Views/                 # Interfaces utilisateur
├── MainWindow.xaml        # Fenêtre principale avec 4 onglets
└── App.xaml               # Bootstrap de l'application
```

### Pattern MVVM

L'application utilise le **Community Toolkit MVVM** pour une séparation claire des responsabilités :

- **Models** : Représentent l'état (VirtualSwitch, VirtualMachine, BaseVhdx)
- **Services** : Intègrent PowerShell et logique métier (HyperVService)
- **ViewModels** : Exposent les commandes et propriétés pour la UI (MainViewModel)
- **Views** : Interfaces XAML qui bindent sur le ViewModel

## Utilisation

### 1. Créer un Commutateur Virtuel (vSwitch) NAT

```
1. Ouvrir l'onglet "Commutateurs Virtuels"
2. Cliquer "Créer nouveau commutateur"
3. Entrer les détails :
   - Nom du commutateur
   - Adresse IP (ex: 192.168.100.1)
   - Masque sous-réseau (ex: 255.255.255.0)
4. Cliquer "Créer"
```

### 2. Créer une Image VHDX de Base

```
1. Ouvrir l'onglet "Images VHDX"
2. Cliquer "Créer Image de Base"
3. Sélectionner le fichier ISO de Windows
4. Définir le chemin de destination
5. Laisser le processus créer la VHDX
```

### 3. Créer une Machines Virtuelle avec Disque Différentiel

```
1. Ouvrir l'onglet "Machines Virtuelles"
2. Cliquer "Créer nouvelle machine"
3. Entrer les paramètres :
   - Nom de la machine
   - Mémoire (MB)
   - Nombre de processeurs
   - Commutateur virtuel
   - Image VHDX de base
4. L'application crée un disque différentiel automatiquement
```

### 4. Gérer les Machines Virtuelles

```
- Cliquer "Rafraîchir" pour recharger l'état
- Sélectionner une VM et cliquer "Démarrer"
- Cliquer "Arrêter" pour éteindre une VM
- Cliquer "Supprimer" pour la supprimer
```

## Commandes PowerShell Intégrées

L'application wraps les cmdlets Hyper-V suivants :

| Opération | Cmdlet PowerShell |
|-----------|-------------------|
| Lister les vSwitch | `Get-VMSwitch` |
| Créer un vSwitch NAT | `New-VMSwitch -SwitchType NAT` |
| Lister les VMs | `Get-VM` |
| Créer une VM | `New-VM` |
| Démarrer une VM | `Start-VM` |
| Arrêter une VM | `Stop-VM -Force` |
| Supprimer une VM | `Remove-VM -Force` |
| Créer disque différentiel | `New-VHD -Differencing` |

## Dépendances NuGet

- **Microsoft.WindowsAppSDK** (1.6+) - Framework WinUI3
- **Microsoft.Windows.SDK.BuildTools** - Outils de build
- **CommunityToolkit.Mvvm** (8.2.2) - Support MVVM avec source generators
- **System.Text.Json** - Parsing JSON PowerShell

## Troubleshooting

### L'application ne démarre pas

```
❌ Erreur: "Windows App SDK not found"
✅ Solution: dotnet restore && dotnet build -c Release

❌ Erreur: "Hyper-V not available"
✅ Solution: Activer Hyper-V dans les Fonctionnalités Windows
```

### Aucun vSwitch ne s'affiche

```
❌ Le tableau est vide après "Rafraîchir"
✅ Causes possibles:
   - Aucun vSwitch créé
   - PowerShell ne trouve pas les cmdlets Hyper-V
   - Permissions insuffisantes (lancer en tant qu'admin)
```

### Impossible de créer une VM

```
❌ Erreur "Access Denied"
✅ Solution: Relancer l'application en tant qu'Administrateur
```

## Licence

Ce projet est sous licence MIT - voir le fichier [LICENSE](LICENSE) pour les détails.

## Contact & Support

- **Issues** : [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)
- **Discussions** : [GitHub Discussions](https://github.com/JM2K69/HV-LAB/discussions)

---

**Créé par** : JM2K69  
**Dernière mise à jour** : Juillet 2026
