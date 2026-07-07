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
    [ObservableProperty] private string vmName = "";
    [ObservableProperty] private string? selectedBaseVhdx;
    [ObservableProperty] private string? selectedSwitch;
    [ObservableProperty] private long memoryMB = 2048;
    [ObservableProperty] private int cpuCount = 2;
    [ObservableProperty] private int generation = 2;
    [ObservableProperty] private string vmFolder;
    [ObservableProperty] private string baseVhdxFolder;

    public List<int>  Generations   { get; } = [1, 2];
    public List<long> MemoryOptions { get; } = [512, 1024, 2048, 4096, 8192, 16384];
    public List<int>  CpuOptions    { get; } = [1, 2, 4, 8, 12, 16];

    public CreateVmViewModel()
    {
        vmFolder       = AppSettings.Current.VmsFolder;
        baseVhdxFolder = AppSettings.Current.BaseImagesFolder;
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
            await _hvService.CreateVMWithDifferencingDiskAsync(
                VmName, SelectedBaseVhdx, SelectedSwitch,
                MemoryMB, CpuCount, Generation, VmFolder);
            Status = $"✓ VM '{VmName}' créée avec succès !";
            VmName = "";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
