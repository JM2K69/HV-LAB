using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class CreateVmViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();
    private readonly VhdxService _vhdxService = new();

    [ObservableProperty] private ObservableCollection<string> baseVhdxList = [];
    [ObservableProperty] private ObservableCollection<string> virtualSwitches = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Prêt";

    // VM form
    [ObservableProperty] private string  vmName = "";
    [ObservableProperty] private string? selectedBaseVhdx;
    [ObservableProperty] private string? selectedSwitch;
    [ObservableProperty] private long    memoryMB = 2048;
    [ObservableProperty] private int     cpuCount = 2;
    [ObservableProperty] private int     generation = 2;
    [ObservableProperty] private string  vmFolder;
    [ObservableProperty] private string  baseVhdxFolder;

    // ─── Answer file (injected into the differencing disk at creation time) ────

    [ObservableProperty] private bool   useAnswerFile  = true;
    [ObservableProperty] private string computerName   = "LAB-VM";
    [ObservableProperty] private string adminPassword  = "P@ssw0rd!";
    [ObservableProperty] private string productKey     = "";
    [ObservableProperty] private string uiLanguage     = "fr-FR";
    [ObservableProperty] private string timeZone       = "Romance Standard Time";
    [ObservableProperty] private string answerFilePreview = "";

    public List<string> Languages { get; } = ["fr-FR", "en-US", "en-GB", "de-DE", "es-ES", "it-IT"];
    public List<string> TimeZones { get; } =
    [
        "Romance Standard Time", "W. Europe Standard Time", "Central Europe Standard Time",
        "GMT Standard Time", "Eastern Standard Time", "Central Standard Time",
        "Pacific Standard Time", "Turkey Standard Time"
    ];

    // ─── Hardware options ─────────────────────────────────────────────────────

    public List<int>  Generations   { get; } = [1, 2];
    public List<long> MemoryOptions { get; } = [512, 1024, 2048, 4096, 8192, 16384];
    public List<int>  CpuOptions    { get; } = [1, 2, 4, 8, 12, 16];

    public CreateVmViewModel()
    {
        vmFolder       = AppSettings.Current.VmsFolder;
        baseVhdxFolder = AppSettings.Current.BaseImagesFolder;
        // Pre-seed computer name from VM name when it changes
        UpdateAnswerPreview();
    }

    partial void OnVmNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(ComputerName) || ComputerName == "LAB-VM")
            ComputerName = value.Length > 15 ? value[..15] : value;
    }

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = "(fichier de réponse désactivé)"; return; }
        AnswerFilePreview = AnswerFileGenerator.Generate(BuildConfig());
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        Status = "Chargement…";
        try
        {
            var switches = await _hvService.GetVirtualSwitchesAsync();
            VirtualSwitches.Clear();
            foreach (var sw in switches) VirtualSwitches.Add(sw.Name);

            var images = await _vhdxService.GetBaseVhdxListAsync(BaseVhdxFolder);
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img.FilePath);

            Status = "Prêt";
        }
        catch (Exception ex) { Status = $"Erreur lors du chargement : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (string.IsNullOrWhiteSpace(VmName))                               { Status = "Saisissez un nom."; return; }
        if (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx))
            { Status = "Sélectionnez une image VHDX de base valide."; return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))                       { Status = "Sélectionnez un commutateur."; return; }

        IsLoading = true;
        Status = $"Création de la VM '{VmName}'…";
        try
        {
            string? answerXml = UseAnswerFile
                ? AnswerFileGenerator.Generate(BuildConfig())
                : null;

            await _hvService.CreateVMWithDifferencingDiskAsync(
                VmName, SelectedBaseVhdx, SelectedSwitch,
                MemoryMB, CpuCount, Generation, VmFolder,
                answerXml);

            Status = $"✓ VM '{VmName}' créée avec succès !";
            VmName       = "";
            ComputerName = "LAB-VM";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private AnswerFileConfig BuildConfig() => new()
    {
        ComputerName  = string.IsNullOrWhiteSpace(ComputerName) ? VmName : ComputerName,
        AdminPassword = AdminPassword,
        ProductKey    = ProductKey,
        UILanguage    = UiLanguage,
        InputLocale   = AnswerFileGenerator.GetInputLocale(UiLanguage),
        SystemLocale  = UiLanguage,
        UserLocale    = UiLanguage,
        TimeZone      = TimeZone,
        ImageIndex    = 1,
    };
}

