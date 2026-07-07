# 🏗️ Architecture de HV-LAB Manager

## Vue Générale

```
┌─────────────────────────────────────────────────────────────┐
│                    Application WPF (Interface)              │
│                      MainWindow.xaml                         │
└────────────┬────────────────────────────────────────────────┘
             │
             │ Data Binding
             ▼
┌─────────────────────────────────────────────────────────────┐
│              MainViewModel (MVVM ViewModel)                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Properties:                                         │  │
│  │  • VirtualSwitches (ObservableCollection)            │  │
│  │  • VirtualMachines (ObservableCollection)            │  │
│  │  • BaseVhdxImages (ObservableCollection)             │  │
│  │  • StatusMessage (string)                            │  │
│  │  • IsLoading (bool)                                  │  │
│  │                                                      │  │
│  │  Commands:                                           │  │
│  │  • RefreshVirtualSwitchesCommand                     │  │
│  │  • RefreshVirtualMachinesCommand                     │  │
│  │  • CreateVSwitchCommand                              │  │
│  │  • CreateVMCommand                                   │  │
│  │  • StartVMCommand / StopVMCommand                    │  │
│  │  • DeleteVMCommand                                   │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────┬────────────────────────────────────────────────┘
             │
             │ Utilise
             ▼
┌─────────────────────────────────────────────────────────────┐
│              HyperVService (Services)                       │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  Méthodes:                                           │  │
│  │  • GetVirtualSwitchesAsync()                         │  │
│  │  • CreateVSwitchNATAsync(name, ip, prefix)          │  │
│  │  • GetVirtualMachinesAsync()                         │  │
│  │  • CreateVirtualMachineAsync(vm)                     │  │
│  │  • StartVMAsync(vmName)                              │  │
│  │  • StopVMAsync(vmName)                               │  │
│  │  • RemoveVMAsync(vmName)                             │  │
│  │  • CreateDifferentialDiskAsync(baseVhdx, output)     │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────┬────────────────────────────────────────────────┘
             │
             │ Exécute PowerShell
             ▼
┌─────────────────────────────────────────────────────────────┐
│              Microsoft Hyper-V (PowerShell)                │
│              Windows Management API                         │
└─────────────────────────────────────────────────────────────┘
```

## Flux de Données

### Récupération de Données (Read)

```
[View: Tableau de Bord]
         ↓ (Click: Rafraîchir)
[ViewModel: RefreshVirtualSwitchesCommand]
         ↓ (Await)
[Service: GetVirtualSwitchesAsync()]
         ↓ (PowerShell)
[Hyper-V: Get-VMSwitch]
         ↓ (JSON Response)
[Service: ParseVirtualSwitches()]
         ↓ (List<VirtualSwitch>)
[ViewModel: ObservableCollection updated]
         ↓ (Data Binding)
[View: DataGrid refreshed]
         ▲
         │ Affichage
         └─ Utilisateur voit les changements
```

### Création de Ressource (Create)

```
[User Input: Create Button]
         ↓
[Dialog: VSwitchCreateDialog]
         ↓ (Get Values)
[VirtualSwitch Model Created]
         ↓ (Execute Command)
[ViewModel: CreateVSwitchCommand]
         ↓ (Call Service)
[Service: CreateVSwitchNATAsync()]
         ↓ (Generate PowerShell Script)
$script = @"
New-VMSwitch -Name 'Name' -SwitchType Internal
New-NetIPAddress -IPAddress 'IP' -PrefixLength 24
New-NetNat -Name 'nat-Name'
"@
         ↓ (Execute via Process)
[Process: powershell.exe]
         ↓ (Hyper-V API)
[Hyper-V: Creating resources]
         ↓ (Success/Error)
[Service: Return bool]
         ↓ (Update UI)
[ViewModel: StatusMessage updated]
         ↓ (Refresh data)
[Service: GetVirtualSwitchesAsync()]
```

## Modèles de Données

### VirtualSwitch
```csharp
public class VirtualSwitch
{
    public string Name { get; set; }              // "Lab-NAT"
    public string SwitchType { get; set; }        // "Internal", "External"
    public string IpAddress { get; set; }         // "192.168.100.1"
    public string SubnetMask { get; set; }        // "24"
    public string Description { get; set; }       // "NAT Lab Switch"
    public bool Enabled { get; set; }             // true
    public int ConnectedVMs { get; set; }         // 3
}
```

### VirtualMachine
```csharp
public class VirtualMachine
{
    public string Id { get; set; }                // Unique ID
    public string Name { get; set; }              // "VM01"
    public string State { get; set; }             // "Running", "Off"
    public long MemoryMB { get; set; }            // 2048
    public int ProcessorCount { get; set; }       // 2
    public string VSwitchName { get; set; }       // "Lab-NAT"
    public string BaseVhdxPath { get; set; }      // Path to base VHDX
    public string DifferentialDiskPath { get; set; } // Path to diff disk
    public DateTime CreatedAt { get; set; }       // Creation timestamp
    public bool IsDynamic { get; set; }           // Dynamic disk?
}
```

### BaseVhdx
```csharp
public class BaseVhdx
{
    public string Id { get; set; }                // Unique ID
    public string Name { get; set; }              // "Windows-Server-2022"
    public string Path { get; set; }              // Full file path
    public string OSType { get; set; }            // "Windows Server 2022"
    public long SizeGB { get; set; }              // 30
    public DateTime CreatedAt { get; set; }       // Creation time
    public int UsageCount { get; set; }           // # of diff disks
    public string Description { get; set; }       // Optional notes
}
```

## Pattern MVVM

```
┌─────────────────────────────────────────────────────────┐
│                        MODEL                            │
│  ┌─────────────────────────────────────────────────┐   │
│  │  • VirtualSwitch                                │   │
│  │  • VirtualMachine                               │   │
│  │  • BaseVhdx                                     │   │
│  │  (Pure data, no logic)                          │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
           ▲                           ▲
           │                           │
     (CreateS)                    (CreatesS)
           │                           │
┌──────────┴────────────────────────────┴────────────────┐
│                    VIEW MODEL                          │
│  ┌───────────────────────────────────────────────────┐ │
│  │  • Observable Properties                         │ │
│  │  • Relay Commands                                │ │
│  │  • Business Logic Orchestration                  │ │
│  │  • State Management                              │ │
│  └───────────────────────────────────────────────────┘ │
└──────────────────────┬──────────────────────────────────┘
                       │
                  (Binds to)
                       │
        ┌──────────────▼───────────────┐
        │           VIEW               │
        │  ┌────────────────────────┐  │
        │  │  MainWindow.xaml       │  │
        │  │  • UI Controls         │  │
        │  │  • Data Bindings       │  │
        │  │  • User Events         │  │
        │  └────────────────────────┘  │
        └──────────────────────────────┘
```

## Interaction Hyper-V

### Hiérarchie des Commandes PowerShell

```
Hyper-V Host
│
├─ VMSwitch (Commutateur Virtuel)
│  ├─ Get-VMSwitch          ← Récupérer la liste
│  ├─ New-VMSwitch          ← Créer
│  └─ Remove-VMSwitch       ← Supprimer
│
├─ VM (Machine Virtuelle)
│  ├─ Get-VM                ← Lister les VMs
│  ├─ New-VM                ← Créer une VM
│  ├─ Start-VM              ← Démarrer
│  ├─ Stop-VM               ← Arrêter
│  ├─ Remove-VM             ← Supprimer
│  └─ Add-VMHardDiskDrive   ← Attacher un disque
│
├─ VHD (Disque Virtuel)
│  ├─ New-VHD               ← Créer (différentiel)
│  ├─ Get-VHD               ← Informations
│  └─ Remove-VHD            ← Supprimer
│
└─ Network
   ├─ New-NetIPAddress      ← Configurer IP
   ├─ New-NetNat            ← Configurer NAT
   └─ Get-NetAdapter        ← Lister adaptateurs
```

### Exécution de PowerShell

```
[C# Application]
         ↓
[Create ProcessStartInfo]
  FileName: "powershell.exe"
  Arguments: "-NoProfile -Command \"<script>\""
  RedirectStandardOutput: true
         ↓
[Start Process]
         ↓
[Read StandardOutput]
         ↓
[Wait for Exit]
         ↓
[Parse Output (JSON)]
         ↓
[Return to ViewModel]
```

## Flux d'Exécution Typique

### 1. Démarrage
```
1. App.xaml → InitializeComponent()
2. MainWindow() → Initialize ViewModel
3. ViewModel → Initialize HyperVService
4. Window loaded → No initial data fetch
5. User can click "Refresh" or navigate
```

### 2. Rafraîchir les vSwitches
```
1. User clicks "Rafraîchir"
2. MainWindow.xaml.cs → RefreshSwitchesButton_Click()
3. ViewModel → RefreshVirtualSwitchesCommand.Execute()
4. IsLoading = true
5. StatusMessage = "Chargement..."
6. HyperVService → GetVirtualSwitchesAsync()
7. Execute PowerShell: Get-VMSwitch | ConvertTo-Json
8. Parse JSON response
9. Update ObservableCollection<VirtualSwitch>
10. IsLoading = false
11. UI automatically updates via data binding
```

### 3. Créer une VM
```
1. User clicks "Créer VM"
2. Dialog opens (input form)
3. User enters data and clicks "Créer"
4. MainWindow.xaml.cs → CreateVMButton_Click()
5. Create VirtualMachine model
6. ViewModel → CreateVMCommand.Execute(vm)
7. IsLoading = true
8. HyperVService → CreateVirtualMachineAsync(vm)
9. Generate PowerShell script
10. Execute script via Process
11. Parse result
12. If success:
    - StatusMessage = "VM créée"
    - Trigger RefreshVirtualMachinesCommand
    - Fetch latest VM list
13. Update UI
```

## Performance et Optimisation

### Async/Await
- Toutes les opérations Hyper-V sont asynchrones
- Empêche le gel de l'interface
- Meilleure expérience utilisateur

### ObservableCollection
- Notifications de changement automatiques
- DataGrid se met à jour en temps réel
- Pas besoin de rafraîchir manuellement

### PowerShell Redirection
- Sortie standard redirigée vers StringBuilder
- Pas de fenêtre PowerShell visible
- Processus s'exécute silencieusement

## Points d'Extension Future

### 1. JSON Parsing Avancé
- Actuellement: Commentaires TODO
- À faire: Implémenter avec `System.Text.Json`

### 2. Monitoring en Temps Réel
- Ajouter des timers
- Polling automatique des statuts
- Graphiques de performance

### 3. CLI Alternative
- Exporter HyperVService comme library
- Créer une interface CLI
- Permettre l'automatisation

### 4. API REST
- Serveur HTTP intégré
- Endpoints pour chaque opération
- WebSockets pour live updates

---

**Cette architecture permet une maintenance facile et des extensions futures.**
