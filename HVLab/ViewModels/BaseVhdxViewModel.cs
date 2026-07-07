using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class BaseVhdxViewModel : ObservableObject
{
    private readonly VhdxService _vhdxService = new();

    [ObservableProperty] private ObservableCollection<BaseVhdx> baseVhdxList = [];
    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = "Prêt";
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

            WimAnalysisStatus = $"✓ Index {match.ImageIndex} — {match.ImageName}";
        }
        else
        {
            MatchedWimImage   = null;
            WimAnalysisStatus = $"⚠ Aucun index trouvé pour « {SelectedEdition} »" +
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
            _ = AnalyzeWimAsync();
    }

    // ─── Constructor ──────────────────────────────────────────────────────────

    public BaseVhdxViewModel()
    {
        baseFolder = AppSettings.Current.BaseImagesFolder;
        RefreshEditions();
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = "Recherche des images…";
        try
        {
            var images = await _vhdxService.GetBaseVhdxListAsync(BaseFolder);
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img);
            Status = $"{images.Count} image(s) dans {BaseFolder}";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
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
            WimAnalysisStatus = "Sélectionnez d'abord un fichier WIM/ESD.";
            return;
        }

        IsAnalyzingWim    = true;
        WimAnalysisStatus = "Analyse du fichier WIM en cours…";
        WimAnalyzed       = false;
        WimImages.Clear();
        MatchedWimImage   = null;

        try
        {
            var images = await _vhdxService.GetWimImagesAsync(WimPath);
            foreach (var img in images) WimImages.Add(img);

            if (images.Count == 0)
            {
                WimAnalysisStatus = "⚠ Aucune image trouvée dans ce fichier.";
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
            WimAnalysisStatus = $"✗ Erreur d'analyse : {ex.Message}";
        }
        finally
        {
            IsAnalyzingWim = false;
        }
    }

    [RelayCommand]
    public async Task CreateBaseVhdxAsync()
    {
        if (string.IsNullOrWhiteSpace(OsVersion))
            { Status = "La version OS n'a pas pu être détectée — saisissez-la manuellement."; return; }
        if (string.IsNullOrWhiteSpace(WimPath) || !File.Exists(WimPath))
            { Status = "Sélectionnez un fichier WIM/ESD valide."; return; }

        IsCreating = true; IsLoading = true;
        var vhdxPath = System.IO.Path.Combine(BaseFolder, $"{PreviewFileName}.vhdx");
        try
        {
            BuildStatus = $"Création du VHDX : {System.IO.Path.GetFileName(vhdxPath)}…";
            await _vhdxService.CreateBaseVhdxAsync(vhdxPath, SizeGB);

            BuildStatus = "Application de l'image Windows (quelques minutes)…";
            // No answer file on the base image — it must stay in generalized/sysprep state.
            // unattend.xml will be injected at VM creation time (into the differencing disk).
            await _vhdxService.ApplyWindowsImageAsync(vhdxPath, WimPath, ImageIndex, Generation, answerFileContent: null);

            BuildStatus = "✓ Image créée avec succès !";
            Status = $"Image créée : {vhdxPath}";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            BuildStatus = $"✗ Erreur : {ex.Message}";
            Status = $"Échec : {ex.Message}";
        }
        finally { IsCreating = false; IsLoading = false; }
    }

    public async Task DeleteBaseVhdxAsync(BaseVhdx vhdx)
    {
        IsLoading = true;
        Status = $"Suppression de '{vhdx.Name}'…";
        try
        {
            await Task.Run(() => File.Delete(vhdx.FilePath));
            Status = $"Image '{vhdx.Name}' supprimée.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }
}
