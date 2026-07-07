# HyperV Lab Manager

Une application WinUI3 moderne pour gérer facilement l'infrastructure Microsoft Hyper-V.

## Fonctionnalités

- ✅ **Gestion des Commutateurs Virtuels (vSwitch)** - Créer, configurer et gérer les vSwitch NAT
- ✅ **Gestion du Réseau** - Configuration complète des adresses IP et des sous-réseaux
- ✅ **Gestion des Machines Virtuelles** - Démarrer, arrêter, créer et supprimer des VMs
- ✅ **Disques Différentiels** - Créer des images VHDX de base et déployer rapidement des VMs avec disques différentiels
- ✅ **Interface Moderne** - UI WinUI3 intuitive et réactive
- ✅ **Automatisation PowerShell** - Tous les appels Hyper-V via PowerShell

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
