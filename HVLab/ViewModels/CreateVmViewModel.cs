using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class CreateVmViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();
    private readonly VhdxService _vhdxService = new();

    [ObservableProperty] private ObservableCollection<string> baseVhdxList = [];
    [ObservableProperty] private ObservableCollection<string> virtualSwitches = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isCreating;
    [ObservableProperty] private string status = "Prêt";

    private CancellationTokenSource? _cts;

    // VM form
    [ObservableProperty] private string  vmName = "";
    [ObservableProperty] private string? selectedBaseVhdx;
    [ObservableProperty] private string? selectedSwitch;
    [ObservableProperty] private long    memoryMB = 2048;
    [ObservableProperty] private int     cpuCount = 2;
    [ObservableProperty] private int     generation = 2;
    [ObservableProperty] private string  vmFolder;
    [ObservableProperty] private string  baseVhdxFolder;

    // ─── Bulk mode ──────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   bulkMode   = false;
    [ObservableProperty] private string bulkPrefix = "SRV";
    [ObservableProperty] private int    bulkCount  = 2;

    partial void OnBulkModeChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(VmName))
            VmName = $"{BulkPrefix}-01";
        OnPropertyChanged(nameof(BulkNamesPreview));
    }

    partial void OnBulkPrefixChanged(string value) => OnPropertyChanged(nameof(BulkNamesPreview));
    partial void OnBulkCountChanged(int value)      => OnPropertyChanged(nameof(BulkNamesPreview));

    public string BulkNamesPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BulkPrefix) || BulkCount < 1) return "";
            var names = Enumerable.Range(1, Math.Min(BulkCount, 10))
                                  .Select(i => $"{BulkPrefix}-{i:D2}");
            return BulkCount > 10
                ? string.Join("  ", names) + $"  … (+{BulkCount - 10})"
                : string.Join("  ", names);
        }
    }

    // ─── Answer file (injected into the differencing disk at creation time) ────

    [ObservableProperty] private bool   useAnswerFile  = true;
    [ObservableProperty] private string computerName   = "LAB-VM";
    [ObservableProperty] private string adminPassword  = "";
    [ObservableProperty] private bool   autoLogon      = false;
    [ObservableProperty] private string productKey     = "";
    [ObservableProperty] private KmsKeyEntry? selectedKmsKey;
    [ObservableProperty] private string uiLanguage     = "fr-FR";
    [ObservableProperty] private string timeZone       = "Romance Standard Time";
    [ObservableProperty] private string answerFilePreview = "";
    [ObservableProperty] private bool   buildCbsCache    = false;
    [ObservableProperty] private bool   disableWindowsUpdate = false;
    [ObservableProperty] private bool   showPassword     = false;

    public string PasswordEyeGlyph => ShowPassword ? "\uED1A" : "\uE052";

    partial void OnShowPasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordEyeGlyph));
        UpdateAnswerPreview();
    }

    partial void OnSelectedKmsKeyChanged(KmsKeyEntry? value)
    {
        if (value is not null)
            ProductKey = value.Key;
    }

    private OsFamily _detectedOsFamily = OsFamily.Server;

    partial void OnSelectedBaseVhdxChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var vhdx = BaseVhdx.FromFile(value);

        // Detect OS family from OsIdentifier
        var norm = vhdx.OsIdentifier.ToLowerInvariant();
        _detectedOsFamily = norm.Contains("server") ? OsFamily.Server : OsFamily.Client;

        // Auto-suggest matching KMS key
        var suggested = KmsKeyCatalog.SuggestForOs(vhdx.OsIdentifier);
        if (suggested is not null)
            SelectedKmsKey = suggested;

        // Always refresh unattend preview so client/server profile is applied immediately
        UpdateAnswerPreview();
    }

    public IReadOnlyList<KmsKeyEntry> KmsKeys { get; } = KmsKeyCatalog.All;

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
        if (string.IsNullOrWhiteSpace(ComputerName) || ComputerName == "LAB-VM"
            || ComputerName == InputValidator.ToComputerName(_previousVmName))
            ComputerName = string.IsNullOrWhiteSpace(value) ? "LAB-VM" : InputValidator.ToComputerName(value);
        _previousVmName = value;
    }

    private string _previousVmName = "";

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = "(fichier de réponse désactivé)"; return; }
        var config = BuildConfig();
        // Never show the password in the preview unless the user asked to see it
        if (!ShowPassword && !string.IsNullOrEmpty(config.AdminPassword)) config.AdminPassword = "********";
        AnswerFilePreview = AnswerFileGenerator.Generate(config);
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Returns the first validation error (localized), or null.</summary>
    private string? Validate(IEnumerable<string> vmNames)
    {
        var loc = LocalizationService.Instance;
        foreach (var name in vmNames)
            if (InputValidator.CheckVmName(name) is { } k) return $"{loc[k]} ({name})";
        if (!UseAnswerFile) return null;
        if (string.IsNullOrEmpty(AdminPassword))        return loc["Val_PasswordEmpty"];
        if (!BulkMode && InputValidator.CheckComputerName(ComputerName) is { } c) return loc[c];
        if (InputValidator.CheckProductKey(ProductKey) is { } p) return loc[p];
        return null;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        Status = "Chargement…";
        try
        {
            // Keep the user's selections: the page is cached and reloads on every visit
            var prevSwitch = SelectedSwitch;
            var prevVhdx   = SelectedBaseVhdx;

            var swTask   = _hvService.GetVirtualSwitchesAsync();
            var imgTask  = _vhdxService.GetBaseVhdxListAsync(BaseVhdxFolder);
            await Task.WhenAll(swTask, imgTask);

            var switches = swTask.Result;
            VirtualSwitches.Clear();
            foreach (var sw in switches) VirtualSwitches.Add(sw.Name);

            var images = imgTask.Result;
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img.FilePath);

            if (prevSwitch is not null && VirtualSwitches.Contains(prevSwitch)) SelectedSwitch   = prevSwitch;
            if (prevVhdx   is not null && BaseVhdxList.Contains(prevVhdx))     SelectedBaseVhdx = prevVhdx;

            Status = "Prêt";
        }
        catch (Exception ex) { Status = $"Erreur lors du chargement : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx))
            { Status = "Sélectionnez une image VHDX de base valide."; return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))
            { Status = "Sélectionnez un commutateur."; return; }

        if (BulkMode)
        {
            if (string.IsNullOrWhiteSpace(BulkPrefix)) { Status = "Saisissez un préfixe."; return; }
            if (BulkCount < 1)                         { Status = "La quantité doit être ≥ 1."; return; }

            var names = Enumerable.Range(1, BulkCount).Select(i => $"{BulkPrefix}-{i:D2}").ToList();
            if (Validate(names) is { } err) { Status = err; return; }

            IsLoading = true; IsCreating = true;
            _cts = new CancellationTokenSource();
            var created = new List<string>();
            var errors  = new List<string>();
            try
            {
                for (int i = 0; i < names.Count; i++)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    string name = names[i];
                    Status = $"Création {i + 1}/{names.Count} : '{name}'…";
                    try
                    {
                        string? answerXml = UseAnswerFile
                            ? AnswerFileGenerator.Generate(BuildConfig(InputValidator.ToComputerName(name)))
                            : null;

                        await _hvService.CreateVMWithDifferencingDiskAsync(
                            name, SelectedBaseVhdx, SelectedSwitch,
                            MemoryMB, CpuCount, Generation, VmFolder,
                            answerXml, secureBoot: true, pxeBoot: false, ct: _cts.Token);

                        created.Add(name);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{name}: {ex.Message}"); }
                }

                if (errors.Count == 0)
                    Status = $"✓ {created.Count} VM(s) créées avec succès : {string.Join(", ", created)}";
                else
                    Status = $"⚠ {created.Count} créées, {errors.Count} erreur(s) : {string.Join(" | ", errors)}";
            }
            catch (OperationCanceledException)
            {
                Status = string.Format(LocalizationService.Instance["Op_CancelledBulk"], created.Count, names.Count);
            }
            finally { IsLoading = false; IsCreating = false; _cts.Dispose(); _cts = null; }
        }
        else
        {
            if (Validate([VmName]) is { } err) { Status = err; return; }

            IsLoading = true; IsCreating = true;
            _cts = new CancellationTokenSource();
            Status = $"Création de la VM '{VmName}'…";
            try
            {
                string? answerXml = UseAnswerFile
                    ? AnswerFileGenerator.Generate(BuildConfig())
                    : null;

                await _hvService.CreateVMWithDifferencingDiskAsync(
                    VmName, SelectedBaseVhdx, SelectedSwitch,
                    MemoryMB, CpuCount, Generation, VmFolder,
                    answerXml, secureBoot: true, pxeBoot: false,
                    progress: new Progress<string>(line => Status = line), ct: _cts.Token);

                Status = $"✓ VM '{VmName}' créée avec succès !";
                VmName       = "";
                ComputerName = "LAB-VM";
            }
            catch (OperationCanceledException) { Status = LocalizationService.Instance["Op_Cancelled"]; }
            catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
            finally { IsLoading = false; IsCreating = false; _cts.Dispose(); _cts = null; }
        }
    }

    private AnswerFileConfig BuildConfig(string? computerNameOverride = null) => new()
    {
        ComputerName  = computerNameOverride ?? (string.IsNullOrWhiteSpace(ComputerName) ? VmName : ComputerName),
        AdminPassword = AdminPassword,
        AutoLogon     = AutoLogon,
        ProductKey    = ProductKey,
        UILanguage    = UiLanguage,
        InputLocale   = AnswerFileGenerator.GetInputLocale(UiLanguage),
        SystemLocale  = UiLanguage,
        UserLocale    = UiLanguage,
        TimeZone      = TimeZone,
        ImageIndex    = 1,
        BuildCbsCache = BuildCbsCache,
        DisableWindowsUpdate = DisableWindowsUpdate,
        OsFamily      = _detectedOsFamily,
    };
}

