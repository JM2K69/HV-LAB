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
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Prêt";
    [ObservableProperty] private string baseFolder = @"C:\HV-LAB\BaseImages";

    // Create form
    [ObservableProperty] private string vhdxName = "";
    [ObservableProperty] private string wimPath = "";
    [ObservableProperty] private int imageIndex = 1;
    [ObservableProperty] private int sizeGB = 80;
    [ObservableProperty] private int generation = 2;
    [ObservableProperty] private bool useAnswerFile = true;
    [ObservableProperty] private bool isCreating;
    [ObservableProperty] private string buildStatus = "";

    // Answer file form
    [ObservableProperty] private string computerName = "LAB-VM";
    [ObservableProperty] private string adminPassword = "P@ssw0rd!";
    [ObservableProperty] private string productKey = "";
    [ObservableProperty] private string uiLanguage = "fr-FR";
    [ObservableProperty] private string timeZone = "Romance Standard Time";
    [ObservableProperty] private string answerFilePreview = "";

    public List<string> Languages  { get; } = ["fr-FR", "en-US", "en-GB", "de-DE", "es-ES", "it-IT"];
    public List<string> TimeZones  { get; } =
    [
        "Romance Standard Time", "W. Europe Standard Time", "Central Europe Standard Time",
        "GMT Standard Time", "Eastern Standard Time", "Central Standard Time",
        "Pacific Standard Time", "Turkey Standard Time"
    ];
    public List<int> Generations { get; } = [1, 2];

    public BaseVhdxViewModel() => UpdateAnswerPreview();

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

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = "(fichier de réponse désactivé)"; return; }
        AnswerFilePreview = AnswerFileGenerator.Generate(BuildConfig());
    }

    [RelayCommand]
    public async Task CreateBaseVhdxAsync()
    {
        if (string.IsNullOrWhiteSpace(VhdxName)) { Status = "Saisissez un nom."; return; }
        if (string.IsNullOrWhiteSpace(WimPath) || !File.Exists(WimPath))
        { Status = "Sélectionnez un fichier WIM/ESD valide."; return; }

        IsCreating = true; IsLoading = true;
        var vhdxPath = Path.Combine(BaseFolder, $"{VhdxName}.vhdx");
        try
        {
            BuildStatus = "Création du VHDX…";
            await _vhdxService.CreateBaseVhdxAsync(vhdxPath, SizeGB);

            string? answerXml = UseAnswerFile ? AnswerFileGenerator.Generate(BuildConfig()) : null;

            BuildStatus = "Application de l'image Windows (quelques minutes)…";
            await _vhdxService.ApplyWindowsImageAsync(vhdxPath, WimPath, ImageIndex, Generation, answerXml);

            BuildStatus = "✓ Image créée avec succès !";
            Status = $"Image '{VhdxName}' disponible : {vhdxPath}";
            await RefreshAsync();
        }
        catch (Exception ex) { BuildStatus = $"✗ Erreur : {ex.Message}"; Status = $"Échec : {ex.Message}"; }
        finally { IsCreating = false; IsLoading = false; }
    }

    public async Task DeleteBaseVhdxAsync(BaseVhdx vhdx)
    {
        IsLoading = true;
        Status = $"Suppression de '{vhdx.Name}'…";
        try
        {
            await Task.Run(() => File.Delete(vhdx.Path));
            Status = $"Image '{vhdx.Name}' supprimée.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    private AnswerFileConfig BuildConfig() => new()
    {
        ComputerName = ComputerName,
        AdminPassword = AdminPassword,
        ProductKey = ProductKey,
        UILanguage = UiLanguage,
        InputLocale = AnswerFileGenerator.GetInputLocale(UiLanguage),
        SystemLocale = UiLanguage,
        UserLocale = UiLanguage,
        TimeZone = TimeZone,
        ImageIndex = ImageIndex,
    };
}
