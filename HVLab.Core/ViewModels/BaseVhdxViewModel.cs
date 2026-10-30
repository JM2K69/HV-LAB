using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class BaseVhdxViewModel : PageViewModelBase
{
    private readonly IVhdxService   _vhdxService;
    private readonly IDialogService _dialogs;

    [ObservableProperty] private ObservableCollection<BaseVhdx> baseVhdxList = [];

    public bool IsImageListEmpty => IsEmptyAfterLoad(BaseVhdxList.Count);
    protected override void OnEmptyStateChanged() => OnPropertyChanged(nameof(IsImageListEmpty));
    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = T("St_Ready");
    [ObservableProperty] private string baseFolder;

    // ─── Structured form fields ──────────────────────────────────────────────

    [ObservableProperty] private string selectedOsFamily  = "WindowsServer2025";
    [ObservableProperty] private string selectedEdition   = "Standard";
    [ObservableProperty] private bool   desktopExperience = true;
    [ObservableProperty] private string osVersion         = "";
    [ObservableProperty] private int    sizeGB            = 50;
    [ObservableProperty] private int    generation        = 2;
    [ObservableProperty] private int    imageIndex        = 1;
    [ObservableProperty] private string wimPath           = "";

    // ─── WIM analysis results ────────────────────────────────────────────────

    /// <summary>All image entries found in the selected WIM/ESD file.</summary>
    public ObservableCollection<WimImageInfo> WimImages { get; } = [];

    [ObservableProperty] private bool   isAnalyzingWim;
    [ObservableProperty] private string wimAnalysisStatus = "";
    [ObservableProperty] private bool   wimAnalyzed;

    /// <summary>The WIM entry that currently matches the selected edition.</summary>
    [ObservableProperty] private WimImageInfo? matchedWimImage;

    // ─── Answer file ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool   isCreating;
    [ObservableProperty] private string buildStatus = "";

    // ─── Dynamic edition list ─────────────────────────────────────────────────

    public ObservableCollection<string> AvailableEditions { get; } = [];
    public bool ShowDesktopExperience { get; private set; }

    // ─── OS catalogue ────────────────────────────────────────────────────────

    public List<string> OsFamilies { get; } =
    [
        "Windows11",
        "Windows10",
        "WindowsServer2025",
        "WindowsServer2022",
        "WindowsServer2019",
        "WindowsServer2016",
    ];

    private static readonly string[] ClientEditions = ["Pro", "Enterprise", "Education", "ProEducation", "ProWorkstation"];
    private static readonly string[] ServerEditions = ["Standard", "Datacenter", "StandardCore", "DatacenterCore"];

    private static bool IsServerFamily(string family) =>
        family.StartsWith("WindowsServer", StringComparison.OrdinalIgnoreCase);

    private bool ComputeShowDesktopExperience() =>
        IsServerFamily(SelectedOsFamily) && !SelectedEdition.EndsWith("Core");

    private void RefreshEditions()
    {
        var editions = IsServerFamily(SelectedOsFamily) ? ServerEditions : ClientEditions;
        AvailableEditions.Clear();
        foreach (var e in editions) AvailableEditions.Add(e);

        if (!AvailableEditions.Contains(SelectedEdition))
            SelectedEdition = AvailableEditions.FirstOrDefault() ?? "";

        ShowDesktopExperience = ComputeShowDesktopExperience();
        OnPropertyChanged(nameof(ShowDesktopExperience));
        OnPropertyChanged(nameof(PreviewFileName));

        if (WimAnalyzed) AutoMatchWimIndex();
    }

    // ─── Option lists ─────────────────────────────────────────────────────────

    public List<string> Languages { get; } = ["fr-FR", "en-US", "en-GB", "de-DE", "es-ES", "it-IT"];
    public List<string> TimeZones { get; } =
    [
        "Romance Standard Time", "W. Europe Standard Time", "Central Europe Standard Time",
        "GMT Standard Time", "Eastern Standard Time", "Central Standard Time",
        "Pacific Standard Time", "Turkey Standard Time"
    ];
    public List<int> Generations { get; } = [1, 2];

    // ─── Computed filename preview ────────────────────────────────────────────

    /// <summary>
    /// BASE_WindowsServer2025Standard(DesktopExperience)_10.0.26100.4652_50
    /// </summary>
    public string PreviewFileName
    {
        get
        {
            var edition = SelectedEdition;
            if (ShowDesktopExperience && DesktopExperience)
                edition += "(DesktopExperience)";
            var ver = string.IsNullOrWhiteSpace(OsVersion) ? "0.0.0.0" : OsVersion.Trim();
            return BaseVhdx.BuildFileName($"{SelectedOsFamily}{edition}", ver, SizeGB);
        }
    }

    // ─── WIM auto-match ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds the best WIM index for the current edition/desktop-experience and
    /// updates <see cref="ImageIndex"/>, <see cref="OsVersion"/>, and status.
    /// </summary>
    private void AutoMatchWimIndex()
    {
        if (WimImages.Count == 0) { MatchedWimImage = null; return; }

        var match = WimImages.FirstOrDefault(img =>
            img.MatchesEdition(SelectedEdition, DesktopExperience && ShowDesktopExperience));

        if (match is not null)
        {
            MatchedWimImage = match;
            ImageIndex      = match.ImageIndex;

            if (string.IsNullOrWhiteSpace(OsVersion) || OsVersion == "0.0.0.0")
                OsVersion = match.Version;

            WimAnalysisStatus = T("BV_WimMatch", match.ImageIndex, match.ImageName);
        }
        else
        {
            MatchedWimImage   = null;
            WimAnalysisStatus = T("BV_WimNoMatch", SelectedEdition) +
                                (ShowDesktopExperience ? $" (Desktop Experience : {DesktopExperience})" : "");
        }
    }

    // ─── Property change hooks ────────────────────────────────────────────────

    partial void OnSelectedOsFamilyChanged(string value) => RefreshEditions();

    partial void OnSelectedEditionChanged(string value)
    {
        ShowDesktopExperience = ComputeShowDesktopExperience();
        OnPropertyChanged(nameof(ShowDesktopExperience));
        OnPropertyChanged(nameof(PreviewFileName));
        if (WimAnalyzed) AutoMatchWimIndex();
    }

    partial void OnDesktopExperienceChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewFileName));
        if (WimAnalyzed) AutoMatchWimIndex();
    }

    partial void OnOsVersionChanged(string value) => OnPropertyChanged(nameof(PreviewFileName));
    partial void OnSizeGBChanged(int value)        => OnPropertyChanged(nameof(PreviewFileName));

    /// <summary>Trigger WIM analysis automatically when a file path is set.</summary>
    partial void OnWimPathChanged(string value)
    {
        WimAnalyzed       = false;
        WimAnalysisStatus = "";
        MatchedWimImage   = null;
        WimImages.Clear();

        if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
            AnalyzeWimCommand.Execute(null);
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public BaseVhdxViewModel(IVhdxService vhdxService, IDialogService dialogs)
    {
        _vhdxService = vhdxService;
        _dialogs     = dialogs;
        baseFolder = AppSettings.Current.BaseImagesFolder;
        RefreshEditions();
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = T("BV_Searching");
        try
        {
            var images = await _vhdxService.GetBaseVhdxListAsync(BaseFolder);
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img);
            Status = T("BV_ImagesIn", images.Count, BaseFolder);
            MarkLoaded();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Analyses the WIM/ESD file: populates <see cref="WimImages"/>,
    /// fills <see cref="OsVersion"/> and auto-selects <see cref="ImageIndex"/>.
    /// </summary>
    [RelayCommand]
    public async Task AnalyzeWimAsync()
    {
        if (string.IsNullOrWhiteSpace(WimPath) || !File.Exists(WimPath))
        {
            WimAnalysisStatus = T("BV_SelectWimFirst");
            return;
        }

        IsAnalyzingWim    = true;
        WimAnalysisStatus = T("BV_Analyzing");
        WimAnalyzed       = false;
        WimImages.Clear();
        MatchedWimImage   = null;

        try
        {
            var images = await _vhdxService.GetWimImagesAsync(WimPath);
            foreach (var img in images) WimImages.Add(img);

            if (images.Count == 0)
            {
                WimAnalysisStatus = T("BV_NoImagesInWim");
                return;
            }

            // Version is the same for all indexes in one WIM
            var detectedVersion = images
                .FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Version))?.Version
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(detectedVersion))
                OsVersion = detectedVersion;

            WimAnalyzed = true;
            AutoMatchWimIndex();
        }
        catch (Exception ex)
        {
            WimAnalysisStatus = T("BV_AnalyzeError", ex.Message);
        }
        finally
        {
            IsAnalyzingWim = false;
        }
    }


    private CancellationTokenSource? _cts;

    [RelayCommand]
    private void CancelBuild() => _cts?.Cancel();

    [RelayCommand]
    public async Task CreateBaseVhdxAsync()
    {
        if (string.IsNullOrWhiteSpace(OsVersion))
            { Status = T("BV_NoOsVersion"); return; }
        if (string.IsNullOrWhiteSpace(WimPath) || !File.Exists(WimPath))
            { Status = T("BV_SelectWim"); return; }

        var vhdxPath = System.IO.Path.Combine(BaseFolder, $"{PreviewFileName}.vhdx");
        // Overwriting breaks every differencing disk built on this image: ask first
        if (File.Exists(vhdxPath)
            && !await _dialogs.ConfirmAsync(T("BV_OverwriteTitle"),
                   T("BV_OverwriteConfirm", System.IO.Path.GetFileName(vhdxPath)), T("BV_BtnOverwrite")))
            return;

        // Build into a temporary file (not matching BASE_*.vhdx) and move it into place only on success,
        // so a failed build never leaves a partial image listed as valid.
        var buildPath = System.IO.Path.Combine(BaseFolder, $"~building_{PreviewFileName}.vhdx");
        IsCreating = true; IsLoading = true;
        ErrorMessage = string.Empty;
        _cts = new CancellationTokenSource();
        var imageApplied = false;
        try
        {
            if (File.Exists(buildPath))
                await _vhdxService.DiscardPartialVhdxAsync(buildPath);

            BuildStatus = T("BV_CreatingVhdx", System.IO.Path.GetFileName(vhdxPath));
            await _vhdxService.CreateBaseVhdxAsync(buildPath, SizeGB);

            BuildStatus = T("BV_Applying");
            // No answer file on the base image — it must stay in generalized/sysprep state.
            // unattend.xml will be injected at VM creation time (into the differencing disk).
            await _vhdxService.ApplyWindowsImageAsync(buildPath, WimPath, ImageIndex, Generation, answerFileContent: null,
                progress: new Progress<string>(line => BuildStatus = TranslateProgress(line)), ct: _cts.Token);
            imageApplied = true;

            // The VHDX is detached asynchronously after Dismount-DiskImage: wait for it, then rename with retries
            await _vhdxService.EnsureDetachedAsync(buildPath);
            await MoveWithRetryAsync(buildPath, vhdxPath);

            BuildStatus = T("BV_BuildOk");
            Status = T("BV_Created", vhdxPath);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException;
            BuildStatus = cancelled ? T("Op_Cancelled") : T("BV_BuildError", ex.Message);
            Status      = cancelled ? T("Op_Cancelled") : string.Empty;
            if (!cancelled)
            {
                // Persistent red banner + full PowerShell output in %LocalAppData%\HV-LAB\logs for diagnosis
                CrashLogger.Log($"Base image build failed ({vhdxPath})",
                                ex is PowerShellException pse ? new Exception($"{pse.Message}\n{pse.Details}", ex) : ex);
                ErrorMessage = imageApplied
                    ? T("BV_RenameFailed", System.IO.Path.GetFileName(buildPath), System.IO.Path.GetFileName(vhdxPath), ex.Message)
                    : T("BV_BuildFailedLog", ex.Message, CrashLogger.LogFolder);
            }
            // A fully applied image is never deleted (only the final rename failed); otherwise discard the partial disk
            if (!imageApplied)
                try { await _vhdxService.DiscardPartialVhdxAsync(buildPath); } catch { /* best effort */ }
        }
        finally { IsCreating = false; IsLoading = false; _cts.Dispose(); _cts = null; }
    }

    private static async Task MoveWithRetryAsync(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { File.Move(from, to, overwrite: true); return; }
            catch (IOException) when (attempt < 20) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) when (attempt < 20) { await Task.Delay(500); }
        }
    }

    [RelayCommand]
    public async Task DeleteBaseVhdxAsync(BaseVhdx vhdx)
    {
        if (!await _dialogs.ConfirmAsync(T("BV_DeleteTitle"), $"{T("BV_DeleteConfirm")} « {vhdx.Name}.vhdx » ?", T("Btn_Delete")))
            return;
        IsLoading = true;
        Status = T("BV_Deleting", vhdx.Name);
        try
        {
            await Task.Run(() => File.Delete(vhdx.FilePath));
            Status = T("BV_Deleted", vhdx.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
    }
}
