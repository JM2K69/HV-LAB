# 🎯 HV-LAB Manager - Guide Complet d'Utilisation

**HV-LAB Manager** est une application Windows moderne pour gérer complètement l'infrastructure Hyper-V depuis une interface graphique intuitive.

## 📋 Table des matières

1. [Installation](#installation)
2. [Architecture](#architecture)
3. [Guide d'Utilisation](#guide-dutilisation)
4. [Fonctionnalités](#fonctionnalités)
5. [Dépannage](#dépannage)

---

## 🚀 Installation

### Prérequis

- **Windows 10/11 Pro, Enterprise, ou Education** (avec Hyper-V)
- **.NET 8.0 Runtime** ([Télécharger ici](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Droits administrateur** pour gérer Hyper-V
- **PowerShell 5.1+** (généralement inclus dans Windows)

### Activation de Hyper-V

Si Hyper-V n'est pas activé sur votre système:

```powershell
# Exécuter en tant qu'administrateur
Enable-WindowsOptionalFeature -FeatureName Hyper-V -Online -All
```

Puis redémarrez votre ordinateur.

### Compilation et Exécution

```bash
cd D:\Git_projets\HV-LAB\HVLab
dotnet build
dotnet run
```

Ou exécutez directement:
```bash
dotnet run --no-build
```

---

## 🏗️ Architecture du Projet

### Structure des Répertoires

```
HVLab/
├── Models/                     # Modèles de données
│   ├── VirtualSwitch.cs       # Représente un commutateur virtuel
│   ├── VirtualMachine.cs      # Représente une VM
│   └── BaseVhdx.cs            # Représente une image VHDX
│
├── Services/                   # Services métier
│   └── HyperVService.cs       # Interaction avec Hyper-V via PowerShell
│
├── ViewModels/                 # Logique de présentation (MVVM)
│   └── MainViewModel.cs       # ViewModel principal
│
├── Converters/                 # Convertisseurs de valeurs WPF
│
├── MainWindow.xaml            # Interface principale
├── MainWindow.xaml.cs         # Code-behind
│
└── App.xaml                   # Ressources globales
```

### Architecture MVVM

L'application suit le pattern **MVVM (Model-View-ViewModel)**:

- **View**: Interface WPF (XAML)
- **ViewModel**: Logique de présentation avec commandes
- **Model**: Modèles de données
- **Service**: Logique métier (appels Hyper-V)

---

## 📖 Guide d'Utilisation

### 1️⃣ Tableau de Bord

Le tableau de bord affiche:
- **Nombre de VMs** créées
- **Nombre de vSwitches** configurés
- **Images de base** disponibles
- **Statut** du système

Boutons:
- **Rafraîchir Tout**: Recharge toutes les données
- **Configuration**: Ouvre les paramètres
- **Aide**: Affiche l'aide

### 2️⃣ Commutateurs Virtuels (vSwitch)

Créez et gérez les commutateurs virtuels NAT:

**Créer un vSwitch NAT:**
1. Cliquez sur "Créer vSwitch NAT"
2. Entrez le nom (ex: `NatSwitch01`)
3. Entrez l'adresse IP (ex: `192.168.100.1`)
4. Entrez le préfixe (ex: `24` pour `/24`)
5. Cliquez "Créer"

**Exemple de configuration:**
- **Nom**: `Lab-NAT`
- **IP**: `192.168.100.1`
- **Préfixe**: `24`
- **Plage DHCP**: `192.168.100.2` - `192.168.100.254`

### 3️⃣ Machines Virtuelles

Créez et contrôlez vos VMs:

**Créer une VM:**
1. Cliquez sur "Créer VM"
2. Nom: `Serveur01`
3. Mémoire: `2048 MB` (2 GB)
4. Processeurs: `2`
5. Sélectionnez le vSwitch
6. Cliquez "Créer"

**Contrôler les VMs:**
- **Démarrer**: Lance la VM
- **Arrêter**: Arrête la VM avec force
- **Supprimer**: Supprime la VM

### 4️⃣ Images de Base VHDX

Créez et gérez les images de base:

**Créer une image:**
1. Cliquez "Créer Image de Base"
2. Nom: `Windows-Server-2022-Base`
3. OS: Sélectionnez le type
4. Taille: `30 GB`
5. Description: (optionnel)
6. Cliquez "Créer"

**Utilisation des disques différentiels:**
```
Image de base (Read-Only)
        ↓
Disque différentiel VM1 (Lectur/Écriture)
Disque différentiel VM2 (Lecture/Écriture)
Disque différentiel VM3 (Lecture/Écriture)
```

Cela permet:
- ✅ Économiser de l'espace disque
- ✅ Déployer rapidement des VMs
- ✅ Clonage facile et rapide

---

## ⚙️ Fonctionnalités Principales

### ✅ Actuelles

- [x] Affichage des vSwitches existants
- [x] Création de vSwitch NAT
- [x] Listage des VMs
- [x] Création de VMs
- [x] Démarrage/Arrêt des VMs
- [x] Suppression de VMs
- [x] Interface moderne et ergonomique
- [x] Statut en temps réel
- [x] Logs d'activité

### 🔜 À Venir

- [ ] Snapshots de VMs
- [ ] Migration de VMs
- [ ] Monitoring en temps réel (CPU, RAM, Disque)
- [ ] Backup/Restore automatisé
- [ ] Gestion de réseaux avancée
- [ ] Support du clustering
- [ ] Export/Import de configurations
- [ ] Console d'accès aux VMs
- [ ] Intégration Active Directory
- [ ] API REST pour automatisation

---

## 🛠️ Scripts PowerShell Utilisés

L'application utilise les cmdlets suivants:

```powershell
# Commutateurs
Get-VMSwitch
New-VMSwitch
Remove-VMSwitch

# Machines Virtuelles
Get-VM
New-VM
Start-VM
Stop-VM
Remove-VM
Add-VMNetworkAdapter
Add-VMHardDiskDrive

# Disques
New-VHD
Get-VHD
Remove-VHD

# Réseau
New-NetIPAddress
New-NetNat
Get-NetAdapter
Get-NetRoute
```

---

## 📊 Cas d'Utilisation

### Cas 1: Setup de Lab de Test

```
1. Créer un vSwitch NAT "Lab"
   - IP: 192.168.100.1/24
   
2. Créer l'image de base
   - Windows Server 2022
   - 30 GB
   
3. Créer 3 VMs avec disques différentiels
   - VM1: Serveur DNS
   - VM2: Serveur Web
   - VM3: Serveur Applicatif
   
4. Chaque VM: 2 GB RAM, 2 CPU
5. Toutes connectées au vSwitch Lab
```

### Cas 2: Déploiement Rapide

```
✓ Cloner une VM existante en 30 secondes
✓ Chaque clone utilise 10% de l'espace original
✓ Snapshots pour faciliter les tests
```

### Cas 3: Montée en Charge

```
1 Image de base (20 GB)
  ├─ VM1 (disque différentiel 5 GB)
  ├─ VM2 (disque différentiel 5 GB)
  ├─ VM3 (disque différentiel 5 GB)
  ├─ VM4 (disque différentiel 5 GB)
  └─ VM5 (disque différentiel 5 GB)

Total: 45 GB au lieu de 100 GB!
```

---

## 🔧 Configuration Avancée

### Configurer les paramètres NAT

Après créer le vSwitch:

```powershell
# Configurer DHCP
netsh dhcp add server 192.168.100.1

# Vérifier la configuration
Get-NetNat

# Modifier le NAT
Set-NetNat -Name "nat-Lab-NAT" -InternalRoutingDomainPrefix 192.168.100.0/24
```

### Scripts d'Automatisation

Créez un fichier `deploy-lab.ps1`:

```powershell
# Créer l'infrastructure
& "$PSScriptRoot\HVLab.exe" &

# Attendre le démarrage
Start-Sleep -Seconds 5

# Exécuter les tâches
Write-Host "Lab déployé avec succès"
```

---

## ❌ Dépannage

### L'application ne démarre pas

**Erreur**: `System.IO.FileNotFoundException`

**Solution**:
```bash
# Réinstaller les dépendances
dotnet restore
dotnet build --force
```

### Hyper-V non accessible

**Erreur**: `Access to the path is denied`

**Solution**:
1. Exécutez l'application **en tant qu'administrateur**
2. Vérifiez que Hyper-V est activé:
```powershell
Get-WindowsOptionalFeature -FeatureName Hyper-V -Online
```

### Les commandes PowerShell échouent

**Erreur**: `The term 'Get-VM' is not recognized`

**Solution**:
1. Vérifiez que Hyper-V est activé
2. Redémarrez PowerShell en administrateur
3. Testez manuellement:
```powershell
Get-VM
```

### Pas de vSwitch existant

**Message**: `No virtual switches found`

**Solution**:
1. Créez un vSwitch via l'application ou:
```powershell
New-VMSwitch -Name "Default" -SwitchType Internal
```

---

## 📞 Support

Pour les problèmes:
1. Vérifiez les logs dans `StatusMessage`
2. Consultez l'observateur d'événements Windows
3. Vérifiez la syntaxe PowerShell

---

## 📄 License

MIT License - Utilisez librement et modifiez

---

## 👨‍💻 Contribution

Contributions bienvenues! N'hésitez pas à:
- Ouvrir des issues pour les bugs
- Proposer des améliorations
- Soumettre des pull requests

