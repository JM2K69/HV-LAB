using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class QuickVmViewModel : ObservableObject
{
    private readonly HyperVService _hvService   = new();
    private readonly VhdxService   _vhdxService = new();

    // ── Size tiles ──────────────────────────────────────────────────────────

    public ObservableCollection<VmSizeItem> SizeItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigVisible))]
    private VmSizeItem? selectedSize;

    public bool ConfigVisible => SelectedSize is not null;

    // ── VM identity ─────────────────────────────────────────────────────────

    [ObservableProperty] private string vmName = "";
    [ObservableProperty] private string? selectedSwitch;
    [ObservableProperty] private string? selectedBaseVhdx;

    // ── Disk mode ────────────────────────────────────────────────────────────
    // true  = Differencing (from a parent base image)
    // false = Blank (new empty VHDX)

    [ObservableProperty] private bool useDifferencingDisk = false;

    partial void OnUseDifferencingDiskChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBlankDisk));
        OnPropertyChanged(nameof(IsDifferencingDisk));
    }

    public bool IsBlankDisk        => !UseDifferencingDisk;
    public bool IsDifferencingDisk =>  UseDifferencingDisk;

    [ObservableProperty] private double diskSizeGB = 40;

    // ── Hardware ────────────────────────────────────────────────────────────

    public List<int> Generations { get; } = [1, 2];

    [ObservableProperty] private int generation = 2;

    partial void OnGenerationChanged(int value)
    {
        // Gen1 has no Secure Boot
        if (value == 1) SecureBoot = false;
        OnPropertyChanged(nameof(SecureBootEnabled));
    }

    // ── Secure Boot ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool secureBoot = true;

    public bool SecureBootEnabled => Generation == 2;

    // ── PXE / Network boot ───────────────────────────────────────────────────

    [ObservableProperty] private bool pxeBoot = false;

    // ── Bulk mode ────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   bulkMode   = false;
    [ObservableProperty] private string bulkPrefix = "SRV";
    [ObservableProperty] private int    bulkCount  = 2;

    partial void OnBulkModeChanged(bool value)      => OnPropertyChanged(nameof(BulkNamesPreview));
    partial void OnBulkPrefixChanged(string value)  => OnPropertyChanged(nameof(BulkNamesPreview));
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

    // ── Data lists ───────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> virtualSwitches = [];
    [ObservableProperty] private ObservableCollection<string> baseVhdxList    = [];

    // ── Status ───────────────────────────────────────────────────────────────

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = "";

    // ── Constructor ──────────────────────────────────────────────────────────

    public QuickVmViewModel()
    {
        foreach (var profile in VmSizeProfile.All)
        {
            VmSizeItem? item = null;
            item = new VmSizeItem
            {
                Profile       = profile,
                SelectCommand = new RelayCommand(() => SelectProfile(item!))
            };
            SizeItems.Add(item);
        }
    }

    private void SelectProfile(VmSizeItem item)
    {
        foreach (var s in SizeItems)
            s.IsSelected = s == item;

        SelectedSize = item;
    }

    [RelayCommand]
    private void SelectBlankMode() => UseDifferencingDisk = false;

    [RelayCommand]
    private void SelectDiffMode() => UseDifferencingDisk = true;

    // ── Load ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var switches = await _hvService.GetVirtualSwitchesAsync();
            VirtualSwitches.Clear();
            foreach (var sw in switches) VirtualSwitches.Add(sw.Name);

            var images = await _vhdxService.GetBaseVhdxListAsync(
                AppSettings.Current.BaseImagesFolder);
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img.FilePath);
        }
        catch (Exception ex) { Status = $"Erreur lors du chargement : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (SelectedSize is null)
            { Status = "Sélectionnez une taille."; return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))
            { Status = "Sélectionnez un commutateur."; return; }

        // Differencing mode needs a valid parent image
        if (UseDifferencingDisk &&
            (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx)))
            { Status = "Sélectionnez une image VHDX de base valide."; return; }

        long   memMB    = SelectedSize.Profile.MemoryMB;
        int    cpuCnt   = SelectedSize.Profile.CpuCount;
        string vmFolder = AppSettings.Current.VmsFolder;

        if (BulkMode)
        {
            if (string.IsNullOrWhiteSpace(BulkPrefix)) { Status = "Saisissez un préfixe."; return; }
            if (BulkCount < 1)                         { Status = "La quantité doit être ≥ 1."; return; }

            IsLoading = true;
            var created = new List<string>();
            var errors  = new List<string>();

            for (int i = 1; i <= BulkCount; i++)
            {
                string name = $"{BulkPrefix}-{i:D2}";
                Status = $"Création {i}/{BulkCount} : '{name}'…";
                try
                {
                    if (UseDifferencingDisk)
                        await _hvService.CreateVMWithDifferencingDiskAsync(
                            name, SelectedBaseVhdx!, SelectedSwitch!,
                            memMB, cpuCnt, Generation, vmFolder,
                            null, SecureBoot, PxeBoot);
                    else
                        await _hvService.CreateBlankVmAsync(
                            name, SelectedSwitch!,
                            memMB, cpuCnt, Generation, vmFolder,
                            (long)DiskSizeGB, SecureBoot, PxeBoot);

                    created.Add(name);
                }
                catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
            }

            IsLoading = false;
            Status = errors.Count == 0
                ? $"✓ {created.Count} VM(s) créées : {string.Join(", ", created)}"
                : $"⚠ {created.Count} créées, {errors.Count} erreur(s) : {string.Join(" | ", errors)}";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(VmName)) { Status = "Saisissez un nom."; return; }

            IsLoading = true;
            Status = $"Création de '{VmName}'…";
            try
            {
                if (UseDifferencingDisk)
                    await _hvService.CreateVMWithDifferencingDiskAsync(
                        VmName, SelectedBaseVhdx!, SelectedSwitch!,
                        memMB, cpuCnt, Generation, vmFolder,
                        null, SecureBoot, PxeBoot);
                else
                    await _hvService.CreateBlankVmAsync(
                        VmName, SelectedSwitch!,
                        memMB, cpuCnt, Generation, vmFolder,
                        (long)DiskSizeGB, SecureBoot, PxeBoot);

                Status = $"✓ VM '{VmName}' créée avec succès !";
                VmName = "";
            }
            catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
            finally { IsLoading = false; }
        }
    }
}
