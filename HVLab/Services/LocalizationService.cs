using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HVLab.Services;

/// <summary>
/// Singleton localization service.
/// Exposes translated strings via an indexer: <c>LocalizationService.Instance["Key"]</c>.
/// Implements <see cref="INotifyPropertyChanged"/> so that x:Bind Mode=OneWay
/// on <c>Loc["Key"]</c> automatically refreshes when the language changes.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    // ── Singleton ────────────────────────────────────────────────────────────

    public static readonly LocalizationService Instance = new();
    private LocalizationService() { }

    // ── State ────────────────────────────────────────────────────────────────

    private string _language = "fr";
    public string CurrentLanguage => _language;

    private Dictionary<string, string> _strings = Strings_fr;

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Indexer (used in x:Bind) ─────────────────────────────────────────────

    /// <summary>Returns the translated string for <paramref name="key"/>.</summary>
    public string this[string key]
        => _strings.TryGetValue(key, out var v) ? v : $"[{key}]";

    /// <summary>Same as indexer but callable from x:Bind expressions.</summary>
    public string GetString(string key)
        => _strings.TryGetValue(key, out var v) ? v : $"[{key}]";

    // ── API ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes the active language and notifies all bindings.
    /// Accepted values: <c>"fr"</c>, <c>"en"</c>.
    /// </summary>
    public void SetLanguage(string language)
    {
        _language = language switch { "en" => "en", _ => "fr" };
        _strings  = _language == "en" ? Strings_en : Strings_fr;
        // Raising "Item[]" refreshes every x:Bind on this[key]
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Notify(nameof(CurrentLanguage));
    }

    // ── FR ───────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Strings_fr = new()
    {
        // ── Navigation ──────────────────────────────────────────────────────
        ["Nav_Dashboard"]    = "Tableau de bord",
        ["Nav_VMs"]          = "Machines Virtuelles",
        ["Nav_Switches"]     = "Commutateurs & NAT",
        ["Nav_BaseVhdx"]     = "Images de base VHDX",
        ["Nav_CreateVM"]     = "Créer une VM",
        ["Nav_Settings"]     = "Paramètres",

        // ── Common ──────────────────────────────────────────────────────────
        ["Btn_Refresh"]      = "Actualiser",
        ["Btn_Browse"]       = "Parcourir…",
        ["Btn_Save"]         = "Enregistrer les paramètres",
        ["Btn_Create"]       = "Créer",
        ["Btn_Delete"]       = "Supprimer",
        ["Btn_Cancel"]       = "Annuler",
        ["Lbl_Status"]       = "Statut",
        ["Lbl_Name"]         = "Nom",

        // ── Dashboard ───────────────────────────────────────────────────────
        ["Dash_Title"]            = "Tableau de bord",
        ["Dash_Overview"]         = "Vue d'ensemble",
        ["Dash_RunningVMs"]       = "VMs en cours",
        ["Dash_StoppedVMs"]       = "VMs arrêtées",
        ["Dash_Switches"]         = "Commutateurs",
        ["Dash_BaseImages"]       = "Images de base",
        ["Dash_QuickStart"]       = "Démarrage rapide",
        ["Dash_Workflow"]         = "Workflow recommandé",
        ["Dash_Step1"]            = "Créer un commutateur interne",
        ["Dash_Step2"]            = "Configurer le NAT sur ce commutateur",
        ["Dash_Step3"]            = "Créer une image de base VHDX",
        ["Dash_Step4"]            = "Créer des VMs avec disques différentiels",
        ["Dash_SysConfig"]        = "Configuration système",
        ["Dash_BaseFolder"]       = "Dossier images de base",
        ["Dash_Elevation"]        = "Élévation : Administrateur",
        ["Dash_HyperVOK"]         = "Hyper-V disponible",
        ["Dash_HyperVWarn"]       = "Hyper-V non disponible",
        ["Dash_HyperVWarnMsg"]    = "Vérifiez que le rôle Hyper-V est installé et que l'application est exécutée en tant qu'Administrateur.",

        // ── Virtual Machines ────────────────────────────────────────────────
        ["VM_Title"]         = "Machines Virtuelles",
        ["VM_ColName"]       = "Nom",
        ["VM_ColState"]      = "État",
        ["VM_ColCPU"]        = "CPU",
        ["VM_ColRAM"]        = "RAM",
        ["VM_ColGen"]        = "Gén.",
        ["VM_ColActions"]    = "Actions",
        ["VM_BtnStart"]      = "Démarrer",
        ["VM_BtnStop"]       = "Arrêter",
        ["VM_BtnDelete"]     = "Supprimer",
        ["VM_DeleteTitle"]   = "Supprimer la VM",
        ["VM_DeleteConfirm"] = "Supprimer définitivement",
        ["VM_FilterAll"]     = "Toutes",
        ["VM_FilterRunning"] = "En cours",
        ["VM_FilterStopped"] = "Arrêtées",

        // ── Virtual Switches ────────────────────────────────────────────────
        ["SW_Title"]         = "Commutateurs & NAT",
        ["SW_TabSwitches"]   = "Commutateurs",
        ["SW_TabNAT"]        = "NAT",
        ["SW_ColName"]       = "Nom",
        ["SW_ColType"]       = "Type",
        ["SW_ColAdapters"]   = "Adaptateurs",
        ["SW_CreateTitle"]   = "Créer un commutateur",
        ["SW_NameLabel"]     = "Nom du commutateur",
        ["SW_TypeLabel"]     = "Type",
        ["SW_BtnCreate"]     = "Créer le commutateur",
        ["SW_BtnDelete"]     = "Supprimer",
        ["SW_DeleteTitle"]   = "Supprimer le commutateur",
        ["SW_DeleteConfirm"] = "Supprimer définitivement",
        ["NAT_CreateTitle"]  = "Créer un réseau NAT",
        ["NAT_NameLabel"]    = "Nom du NAT",
        ["NAT_SubnetLabel"]  = "Sous-réseau (CIDR)",
        ["NAT_BtnCreate"]    = "Créer le NAT",
        ["NAT_BtnDelete"]    = "Supprimer",
        ["NAT_ColName"]      = "Nom",
        ["NAT_ColSubnet"]    = "Sous-réseau",

        // ── Base VHDX ───────────────────────────────────────────────────────
        ["BV_Title"]         = "Images de base VHDX",
        ["BV_ColOS"]         = "Système d'exploitation",
        ["BV_ColVersion"]    = "Version OS",
        ["BV_ColEdition"]    = "Édition",
        ["BV_ColSize"]       = "Taille",
        ["BV_ColDate"]       = "Créé le",
        ["BV_ColAction"]     = "Action",
        ["BV_CreateTitle"]   = "Créer une image de base",
        ["BV_StepWim"]       = "1 — Fichier WIM / ESD source",
        ["BV_BrowseWim"]     = "Sélectionner…",
        ["BV_Analyze"]       = "Analyser",
        ["BV_StepOS"]        = "2 — Système d'exploitation",
        ["BV_OsFamily"]      = "Famille OS",
        ["BV_Edition"]       = "Édition",
        ["BV_DesktopExp"]    = "Desktop Experience",
        ["BV_OsVersion"]     = "Version (ex : 10.0.26100.1)",
        ["BV_StepDisk"]      = "3 — Disque & destination",
        ["BV_Generation"]    = "Génération",
        ["BV_SizeGB"]        = "Taille (Go)",
        ["BV_ImageIndex"]    = "Index WIM",
        ["BV_DestFolder"]    = "Dossier de destination",
        ["BV_Preview"]       = "Aperçu du nom de fichier",
        ["BV_SysprepInfo"]   = "Image en état sysprep",
        ["BV_SysprepMsg"]    = "L'image de base sera créée sans fichier de réponse — elle reste en état sysprep généralisé. Le fichier unattend.xml sera appliqué à chaque VM créée à partir de cette base.",
        ["BV_BtnCreate"]     = "Créer l'image de base",
        ["BV_DeleteTitle"]   = "Supprimer l'image",
        ["BV_DeleteConfirm"] = "Supprimer définitivement",

        // ── Create VM ───────────────────────────────────────────────────────
        ["CVM_Title"]        = "Créer une VM",
        ["CVM_Identity"]     = "Identité",
        ["CVM_VmName"]       = "Nom de la VM",
        ["CVM_Hardware"]     = "Matériel",
        ["CVM_Generation"]   = "Génération",
        ["CVM_RAM"]          = "Mémoire (Mo)",
        ["CVM_CPU"]          = "Processeurs",
        ["CVM_Network"]      = "Réseau",
        ["CVM_Switch"]       = "Commutateur virtuel",
        ["CVM_Storage"]      = "Stockage",
        ["CVM_BaseImage"]    = "Image de base VHDX",
        ["CVM_VmFolder"]     = "Dossier de destination",
        ["CVM_AnswerFile"]   = "Fichier de réponse (unattend.xml)",
        ["CVM_AnswerInfo"]   = "Le fichier unattend.xml sera injecté dans le disque différentiel de la VM au moment de sa création.",
        ["CVM_UseAnswer"]    = "Utiliser un fichier de réponse",
        ["CVM_CompName"]     = "Nom de l'ordinateur (15 car. max)",
        ["CVM_AdminPwd"]     = "Mot de passe Administrateur",
        ["CVM_Locale"]       = "Langue / Locale",
        ["CVM_Timezone"]     = "Fuseau horaire",
        ["CVM_ProductKey"]   = "Clé produit (optionnel)",
        ["CVM_XmlPreview"]   = "Aperçu unattend.xml",
        ["CVM_UpdatePreview"]= "Mettre à jour l'aperçu",
        ["CVM_BtnCreate"]    = "Créer la VM",
        ["CVM_BtnReload"]    = "Recharger",

        // ── Settings ────────────────────────────────────────────────────────
        ["SET_Title"]        = "Paramètres",
        ["SET_SecFiles"]     = "Emplacements des fichiers",
        ["SET_BaseImages"]   = "Images de base VHDX",
        ["SET_BaseImagesDesc"]= "Dossier contenant les fichiers BASE_*.vhdx",
        ["SET_VMs"]          = "Machines virtuelles",
        ["SET_VMsDesc"]      = "Dossier où les disques différentiels et configs des VMs sont créés",
        ["SET_SecSystem"]    = "Système",
        ["SET_PSEngine"]     = "Moteur PowerShell",
        ["SET_PSEngineDesc"] = "Windows PowerShell 5.1 est requis pour le module Hyper-V.",
        ["SET_SecAppearance"]= "Apparence",
        ["SET_Theme"]        = "Thème de l'application",
        ["SET_ThemeDesc"]    = "Par défaut, l'application suit le thème configuré dans Windows.",
        ["SET_Language"]     = "Langue de l'interface",
        ["SET_LanguageDesc"] = "Choisissez la langue de l'interface. Le changement est immédiat.",
        ["SET_SecAbout"]     = "À propos",
        ["SET_AppName"]      = "HV-LAB Manager",
        ["SET_AppDesc"]      = "Gestionnaire Hyper-V · VMs, vSwitch, NAT, VHDX · .NET 8 + WinUI 3",
        ["SET_Persistence"]  = "Persistance",
        ["SET_PersistDesc"]  = "Paramètres sauvegardés dans %LocalAppData%\\HV-LAB\\settings.json",
        ["SET_SavedOk"]      = "✓ Paramètres enregistrés.",
        ["SET_SavedError"]   = "✗ Erreur",
    };

    // ── EN ───────────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> Strings_en = new()
    {
        // ── Navigation ──────────────────────────────────────────────────────
        ["Nav_Dashboard"]    = "Dashboard",
        ["Nav_VMs"]          = "Virtual Machines",
        ["Nav_Switches"]     = "Switches & NAT",
        ["Nav_BaseVhdx"]     = "Base VHDX Images",
        ["Nav_CreateVM"]     = "Create a VM",
        ["Nav_Settings"]     = "Settings",

        // ── Common ──────────────────────────────────────────────────────────
        ["Btn_Refresh"]      = "Refresh",
        ["Btn_Browse"]       = "Browse…",
        ["Btn_Save"]         = "Save settings",
        ["Btn_Create"]       = "Create",
        ["Btn_Delete"]       = "Delete",
        ["Btn_Cancel"]       = "Cancel",
        ["Lbl_Status"]       = "Status",
        ["Lbl_Name"]         = "Name",

        // ── Dashboard ───────────────────────────────────────────────────────
        ["Dash_Title"]            = "Dashboard",
        ["Dash_Overview"]         = "Overview",
        ["Dash_RunningVMs"]       = "Running VMs",
        ["Dash_StoppedVMs"]       = "Stopped VMs",
        ["Dash_Switches"]         = "Switches",
        ["Dash_BaseImages"]       = "Base images",
        ["Dash_QuickStart"]       = "Quick start",
        ["Dash_Workflow"]         = "Recommended workflow",
        ["Dash_Step1"]            = "Create an internal switch",
        ["Dash_Step2"]            = "Configure NAT on that switch",
        ["Dash_Step3"]            = "Create a base VHDX image",
        ["Dash_Step4"]            = "Create VMs with differencing disks",
        ["Dash_SysConfig"]        = "System configuration",
        ["Dash_BaseFolder"]       = "Base images folder",
        ["Dash_Elevation"]        = "Elevation: Administrator",
        ["Dash_HyperVOK"]         = "Hyper-V available",
        ["Dash_HyperVWarn"]       = "Hyper-V unavailable",
        ["Dash_HyperVWarnMsg"]    = "Make sure the Hyper-V role is installed and the application is running as Administrator.",

        // ── Virtual Machines ────────────────────────────────────────────────
        ["VM_Title"]         = "Virtual Machines",
        ["VM_ColName"]       = "Name",
        ["VM_ColState"]      = "State",
        ["VM_ColCPU"]        = "CPU",
        ["VM_ColRAM"]        = "RAM",
        ["VM_ColGen"]        = "Gen.",
        ["VM_ColActions"]    = "Actions",
        ["VM_BtnStart"]      = "Start",
        ["VM_BtnStop"]       = "Stop",
        ["VM_BtnDelete"]     = "Delete",
        ["VM_DeleteTitle"]   = "Delete VM",
        ["VM_DeleteConfirm"] = "Permanently delete",
        ["VM_FilterAll"]     = "All",
        ["VM_FilterRunning"] = "Running",
        ["VM_FilterStopped"] = "Stopped",

        // ── Virtual Switches ────────────────────────────────────────────────
        ["SW_Title"]         = "Switches & NAT",
        ["SW_TabSwitches"]   = "Switches",
        ["SW_TabNAT"]        = "NAT",
        ["SW_ColName"]       = "Name",
        ["SW_ColType"]       = "Type",
        ["SW_ColAdapters"]   = "Adapters",
        ["SW_CreateTitle"]   = "Create a switch",
        ["SW_NameLabel"]     = "Switch name",
        ["SW_TypeLabel"]     = "Type",
        ["SW_BtnCreate"]     = "Create switch",
        ["SW_BtnDelete"]     = "Delete",
        ["SW_DeleteTitle"]   = "Delete switch",
        ["SW_DeleteConfirm"] = "Permanently delete",
        ["NAT_CreateTitle"]  = "Create NAT network",
        ["NAT_NameLabel"]    = "NAT name",
        ["NAT_SubnetLabel"]  = "Subnet (CIDR)",
        ["NAT_BtnCreate"]    = "Create NAT",
        ["NAT_BtnDelete"]    = "Delete",
        ["NAT_ColName"]      = "Name",
        ["NAT_ColSubnet"]    = "Subnet",

        // ── Base VHDX ───────────────────────────────────────────────────────
        ["BV_Title"]         = "Base VHDX Images",
        ["BV_ColOS"]         = "Operating System",
        ["BV_ColVersion"]    = "OS Version",
        ["BV_ColEdition"]    = "Edition",
        ["BV_ColSize"]       = "Size",
        ["BV_ColDate"]       = "Created",
        ["BV_ColAction"]     = "Action",
        ["BV_CreateTitle"]   = "Create a base image",
        ["BV_StepWim"]       = "1 — Source WIM / ESD file",
        ["BV_BrowseWim"]     = "Select…",
        ["BV_Analyze"]       = "Analyze",
        ["BV_StepOS"]        = "2 — Operating system",
        ["BV_OsFamily"]      = "OS family",
        ["BV_Edition"]       = "Edition",
        ["BV_DesktopExp"]    = "Desktop Experience",
        ["BV_OsVersion"]     = "Version (e.g. 10.0.26100.1)",
        ["BV_StepDisk"]      = "3 — Disk & destination",
        ["BV_Generation"]    = "Generation",
        ["BV_SizeGB"]        = "Size (GB)",
        ["BV_ImageIndex"]    = "WIM index",
        ["BV_DestFolder"]    = "Destination folder",
        ["BV_Preview"]       = "File name preview",
        ["BV_SysprepInfo"]   = "Image in sysprep state",
        ["BV_SysprepMsg"]    = "The base image will be created without an answer file — it stays in generalized sysprep state. The unattend.xml will be injected when a VM is created from this base.",
        ["BV_BtnCreate"]     = "Create base image",
        ["BV_DeleteTitle"]   = "Delete image",
        ["BV_DeleteConfirm"] = "Permanently delete",

        // ── Create VM ───────────────────────────────────────────────────────
        ["CVM_Title"]        = "Create a VM",
        ["CVM_Identity"]     = "Identity",
        ["CVM_VmName"]       = "VM name",
        ["CVM_Hardware"]     = "Hardware",
        ["CVM_Generation"]   = "Generation",
        ["CVM_RAM"]          = "Memory (MB)",
        ["CVM_CPU"]          = "Processors",
        ["CVM_Network"]      = "Network",
        ["CVM_Switch"]       = "Virtual switch",
        ["CVM_Storage"]      = "Storage",
        ["CVM_BaseImage"]    = "Base VHDX image",
        ["CVM_VmFolder"]     = "Destination folder",
        ["CVM_AnswerFile"]   = "Answer file (unattend.xml)",
        ["CVM_AnswerInfo"]   = "The unattend.xml will be injected into the VM's differencing disk at creation time.",
        ["CVM_UseAnswer"]    = "Use an answer file",
        ["CVM_CompName"]     = "Computer name (max 15 chars)",
        ["CVM_AdminPwd"]     = "Administrator password",
        ["CVM_Locale"]       = "Language / Locale",
        ["CVM_Timezone"]     = "Time zone",
        ["CVM_ProductKey"]   = "Product key (optional)",
        ["CVM_XmlPreview"]   = "unattend.xml preview",
        ["CVM_UpdatePreview"]= "Update preview",
        ["CVM_BtnCreate"]    = "Create VM",
        ["CVM_BtnReload"]    = "Reload",

        // ── Settings ────────────────────────────────────────────────────────
        ["SET_Title"]        = "Settings",
        ["SET_SecFiles"]     = "File locations",
        ["SET_BaseImages"]   = "Base VHDX images",
        ["SET_BaseImagesDesc"]= "Folder containing BASE_*.vhdx files",
        ["SET_VMs"]          = "Virtual machines",
        ["SET_VMsDesc"]      = "Folder where VM differencing disks and configs are created",
        ["SET_SecSystem"]    = "System",
        ["SET_PSEngine"]     = "PowerShell engine",
        ["SET_PSEngineDesc"] = "Windows PowerShell 5.1 is required for the Hyper-V module.",
        ["SET_SecAppearance"]= "Appearance",
        ["SET_Theme"]        = "Application theme",
        ["SET_ThemeDesc"]    = "By default, the application follows the theme configured in Windows.",
        ["SET_Language"]     = "Interface language",
        ["SET_LanguageDesc"] = "Choose the interface language. The change is immediate.",
        ["SET_SecAbout"]     = "About",
        ["SET_AppName"]      = "HV-LAB Manager",
        ["SET_AppDesc"]      = "Hyper-V Manager · VMs, vSwitch, NAT, VHDX · .NET 8 + WinUI 3",
        ["SET_Persistence"]  = "Persistence",
        ["SET_PersistDesc"]  = "Settings saved in %LocalAppData%\\HV-LAB\\settings.json",
        ["SET_SavedOk"]      = "✓ Settings saved.",
        ["SET_SavedError"]   = "✗ Error",
    };
}
