using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class CreateVmViewModel : VmCreationViewModelBase
{
    // VM form
    [ObservableProperty] private long    memoryMB = 2048;
    [ObservableProperty] private int     cpuCount = 2;
    [ObservableProperty] private int     generation = 2;
    [ObservableProperty] private string  vmFolder;
    [ObservableProperty] private string  baseVhdxFolder;

    protected override string ImagesFolder => BaseVhdxFolder;

    protected override void OnBulkModeChangedCore(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(VmName))
            VmName = $"{BulkPrefix}-01";
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

    protected override void OnSelectedBaseVhdxChangedCore(string? value)
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

    public IReadOnlyList<string> Languages     => LabCatalog.Languages;
    public IReadOnlyList<string> TimeZones     => LabCatalog.TimeZones;

    // ─── Hardware options ─────────────────────────────────────────────────────

    public IReadOnlyList<int>    Generations   => LabCatalog.Generations;
    public IReadOnlyList<long>   MemoryOptions => LabCatalog.MemoryOptions;
    public IReadOnlyList<int>    CpuOptions    => LabCatalog.CpuOptions;

    public CreateVmViewModel(IHyperVService hvService, IVhdxService vhdxService)
        : base(hvService, vhdxService)
    {
        vmFolder       = AppSettings.Current.VmsFolder;
        baseVhdxFolder = AppSettings.Current.BaseImagesFolder;
        // Pre-seed computer name from VM name when it changes
        UpdateAnswerPreview();
    }

    protected override void OnVmNameChangedCore(string value)
    {
        if (string.IsNullOrWhiteSpace(ComputerName) || ComputerName == "LAB-VM"
            || ComputerName == InputValidator.ToComputerName(_previousVmName))
            ComputerName = string.IsNullOrWhiteSpace(value) ? "LAB-VM" : InputValidator.ToComputerName(value);
        _previousVmName = value;
    }

    private string _previousVmName = "";

    // Fields that feed the answer file: any change regenerates the preview (debounced)
    private static readonly HashSet<string> AnswerFields =
    [
        nameof(UseAnswerFile), nameof(VmName), nameof(ComputerName), nameof(AdminPassword),
        nameof(AutoLogon), nameof(ProductKey), nameof(UiLanguage), nameof(TimeZone),
        nameof(BuildCbsCache), nameof(DisableWindowsUpdate),
    ];

    private CancellationTokenSource? _previewCts;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && AnswerFields.Contains(name))
            _ = SchedulePreviewAsync();
    }

    private async Task SchedulePreviewAsync()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        try { await Task.Delay(300, cts.Token); }
        catch (OperationCanceledException) { return; }
        UpdateAnswerPreview();
    }

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = T("St_AnswerDisabled"); return; }
        var config = BuildConfig();
        // Never show the password in the preview unless the user asked to see it
        if (!ShowPassword && !string.IsNullOrEmpty(config.AdminPassword)) config.AdminPassword = "********";
        AnswerFilePreview = AnswerFileGenerator.Generate(config);
    }

    /// <summary>Answer-file checks (VM names are validated by the base class); null when valid.</summary>
    private string? ValidateAnswerFile()
    {
        if (!UseAnswerFile) return null;
        if (string.IsNullOrEmpty(AdminPassword)) return T("Val_PasswordEmpty");
        if (!BulkMode && InputValidator.CheckComputerName(ComputerName) is { } c) return T(c);
        if (InputValidator.CheckProductKey(ProductKey) is { } p) return T(p);
        return null;
    }

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx))
            { Status = T("St_SelectBaseVhdx"); return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))
            { Status = T("St_SelectSwitch"); return; }
        if (GetNamesToCreate() is not { } names) return;
        if (ValidateAnswerFile() is { } err) { Status = err; return; }

        string parent = SelectedBaseVhdx, sw = SelectedSwitch;
        await RunCreationAsync(names,
            (name, progress, ct) =>
            {
                string? answerXml = UseAnswerFile
                    ? AnswerFileGenerator.Generate(BuildConfig(BulkMode ? InputValidator.ToComputerName(name) : null))
                    : null;
                return HvService.CreateVMWithDifferencingDiskAsync(
                    name, parent, sw, MemoryMB, CpuCount, Generation, VmFolder,
                    answerXml, secureBoot: true, pxeBoot: false, progress, ct);
            },
            onSingleSuccess: () => { VmName = ""; ComputerName = "LAB-VM"; });
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

