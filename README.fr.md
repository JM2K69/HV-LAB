# HyperV Lab Manager

> 🇬🇧 [English version available here](README.md)

Une application **WinUI 3 / .NET 8** moderne pour gérer les environnements de laboratoire Microsoft Hyper-V sous Windows.

![Plateforme](https://img.shields.io/badge/plateforme-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![WinUI](https://img.shields.io/badge/UI-WinUI%203-blueviolet)
![Licence](https://img.shields.io/badge/licence-MIT-green)

---

## Fonctionnalités

- ✅ **Gestion des Machines Virtuelles** — Créer, démarrer, arrêter, supprimer et se connecter à la console (Gen 1/2, Secure Boot, ordre de démarrage)
- ✅ **Déploiement par Disques Différenciels** — Déployer des VMs instantanément depuis des images VHDX de base
- ✅ **Unattend.xml Automatique** — Génération de fichiers de réponse compatibles Windows Server et Client
- ✅ **Injection de Clés KMS** — Sélection et injection automatique des clés GVLK selon l'OS choisi
- ✅ **Gestion des vSwitch** — Créer des commutateurs External, Internal et Private avec support VLAN optionnel
- ✅ **Réseau NAT** — Configurer des réseaux NAT avec passerelle et préfixe personnalisés
- ✅ **Console VM** — Connexion directe à la console via `vmconnect` (comme Hyper-V Manager)
- ✅ **Gestion des Images VHDX de Base** — Enregistrer et gérer les images parentes pour les déploiements
- ✅ **Interface Multilingue** — Interface complète en Français 🇫🇷 et Anglais 🇬🇧
- ✅ **Interface Moderne** — WinUI 3 avec backdrop Mica, thème clair/sombre

---

## Prérequis

- **Windows 10 Pro/Enterprise** ou **Windows 11 Pro/Enterprise** (Hyper-V activé)
- **.NET 8.0 Runtime** ou plus récent
- **PowerShell 5.1+** (inclus dans Windows)
- **Hyper-V** activé et accessible
- Privilèges Administrateur (requis pour les opérations Hyper-V)

---

## Démarrage Rapide

```bash
# Cloner le dépôt
git clone https://github.com/JM2K69/HV-LAB.git
cd HV-LAB/HVLab

# Compiler
dotnet build -c Release

# Lancer
dotnet run -c Release
```

> ⚠️ L'application doit être exécutée en tant qu'**Administrateur** pour interagir avec Hyper-V.

---

## Structure du Projet

```
HVLab/
├── Models/                     # Modèles de données
│   ├── VirtualMachine.cs       # Modèle VM (ObservableObject)
│   ├── VirtualSwitch.cs        # Modèle commutateur virtuel
│   ├── BaseVhdx.cs             # Modèle image VHDX de base
│   └── NatNetwork.cs           # Modèle réseau NAT
├── Services/                   # Logique métier
│   ├── HyperVService.cs        # Intégration PowerShell Hyper-V
│   ├── NatService.cs           # Gestion des réseaux NAT
│   ├── LocalizationService.cs  # Localisation FR/EN
│   └── AppSettings.cs          # Paramètres persistants
├── ViewModels/                 # ViewModels MVVM
│   ├── VirtualMachinesViewModel.cs
│   ├── VirtualSwitchesViewModel.cs
│   ├── BaseVhdxViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                      # Pages XAML
│   ├── VirtualMachinesPage.xaml
│   ├── VirtualSwitchesPage.xaml
│   ├── BaseVhdxPage.xaml
│   ├── CreateVmPage.xaml
│   └── SettingsPage.xaml
├── Images/                     # Icônes et assets
├── MainWindow.xaml             # Fenêtre principale avec navigation
└── App.xaml                    # Bootstrap de l'application
```

---

## Utilisation

### 1. Gérer les Machines Virtuelles

1. Naviguer vers **Virtual Machines**
2. Cliquer **Refresh** pour charger la liste actuelle
3. Utiliser les boutons d'action pour **Démarrer**, **Arrêter**, **Connecter** ou **Supprimer** une VM
4. La liste affiche : nom, état, génération, RAM, CPU, commutateur et info VLAN

### 2. Créer une Machine Virtuelle

1. Naviguer vers **Create a VM**
2. Renseigner les paramètres :
   - Nom de la VM et mot de passe administrateur
   - Génération (Gen 1 ou Gen 2) — Gen 2 active le Secure Boot automatiquement
   - RAM, nombre de CPU
   - Sélectionner une image VHDX de base
   - Sélectionner un commutateur virtuel
   - La clé KMS est auto-sélectionnée selon l'OS choisi
3. Cliquer **Créer** — un disque différenciel est créé et `unattend.xml` injecté automatiquement

### 3. Gérer les Commutateurs Virtuels

1. Naviguer vers **Switch & NAT → onglet Switches**
2. Cliquer **Créer** et renseigner :
   - Nom du commutateur
   - Type : External (nécessite un adaptateur réseau), Internal ou Private
   - ID VLAN optionnel
3. Pour supprimer, utiliser le bouton **Supprimer** sur la ligne du commutateur

### 4. Configurer le NAT

1. Naviguer vers **Switch & NAT → onglet NAT**
2. Renseigner :
   - Nom du réseau NAT
   - Sélectionner un commutateur Internal
   - IP passerelle et longueur de préfixe
3. Cliquer **Créer NAT**

### 5. Gérer les Images VHDX de Base

1. Naviguer vers **Base VHDX Images**
2. Enregistrer les images de base (WIM/VHDX) utilisées comme parents pour les disques différenciels
3. Les images sont taguées avec un identifiant OS pour la sélection automatique des clés KMS

### 6. Paramètres

- Choisir la **langue de l'interface** (FR / EN)
- Définir les **dossiers par défaut** pour les VMs et les images de base
- Consulter les infos de l'application et l'auteur dans la section **À propos**

---

## Intégration PowerShell

L'application encapsule les cmdlets Hyper-V PowerShell suivants :

| Opération | Cmdlet PowerShell |
|-----------|-------------------|
| Lister les VMs | `Get-VM` |
| Créer une VM | `New-VM` |
| Démarrer une VM | `Start-VM` |
| Arrêter une VM | `Stop-VM -Force` |
| Supprimer une VM | `Remove-VM -Force` |
| Lister les commutateurs | `Get-VMSwitch` |
| Créer un commutateur | `New-VMSwitch` |
| Configurer le VLAN | `Set-VMNetworkAdapterVlan` |
| Créer un disque différenciel | `New-VHD -Differencing` |
| Appliquer une image WIM | `DISM` via `Convert-WindowsImage` |
| Console VM | `vmconnect.exe` |

---

## Dépendances NuGet

| Package | Version | Usage |
|---------|---------|-------|
| `Microsoft.WindowsAppSDK` | 1.6+ | Framework WinUI 3 |
| `Microsoft.Windows.SDK.BuildTools` | latest | Outils de build |
| `CommunityToolkit.Mvvm` | 8.2.2 | MVVM avec source generators |
| `CommunityToolkit.WinUI.Controls.SettingsControls` | latest | Contrôles Settings UI |

---

## Résolution de Problèmes

### L'application ne démarre pas

```
❌ "Windows App SDK not found"
✅ Exécuter : dotnet restore && dotnet build -c Release

❌ "Hyper-V not available"
✅ Activer Hyper-V dans les Fonctionnalités Windows (optionalfeatures.exe)
```

### Aucune VM ni commutateur ne s'affiche

```
❌ Liste vide après Refresh
✅ Vérifier que :
   - Hyper-V est activé et en cours d'exécution
   - L'application est lancée en Administrateur
   - PowerShell peut exécuter : Get-VM / Get-VMSwitch
```

### Impossible de créer une VM

```
❌ "Access Denied"
✅ Relancer l'application en tant qu'Administrateur
```

---

## Licence

Ce projet est sous licence MIT — voir le fichier [LICENSE](LICENSE) pour les détails.

---

## Auteur & Support

- **Auteur** : [JM2K69](https://github.com/JM2K69)
- **Issues** : [GitHub Issues](https://github.com/JM2K69/HV-LAB/issues)
- **Discussions** : [GitHub Discussions](https://github.com/JM2K69/HV-LAB/discussions)

---

© 2026 JM2K69 — Tous droits réservés
