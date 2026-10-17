using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class QuickVmViewModel : VmCreationViewModelBase
{
    // ── Size tiles ──────────────────────────────────────────────────────────

    public ObservableCollection<VmSizeItem> SizeItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigVisible))]
    private VmSizeItem? selectedSize;

    public bool ConfigVisible => SelectedSize is not null;

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

    public IReadOnlyList<int> Generations => LabCatalog.Generations;

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

    // ── Constructor ──────────────────────────────────────────────────────────

    protected override string ImagesFolder => AppSettings.Current.BaseImagesFolder;

    public QuickVmViewModel(IHyperVService hvService, IVhdxService vhdxService)
        : base(hvService, vhdxService)
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

    // ── Create ────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (SelectedSize is null)
            { Status = T("St_SelectSize"); return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))
            { Status = T("St_SelectSwitch"); return; }

        // Differencing mode needs a valid parent image
        if (UseDifferencingDisk &&
            (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx)))
            { Status = T("St_SelectBaseVhdx"); return; }
        if (GetNamesToCreate() is not { } names) return;

        long   memMB    = SelectedSize.Profile.MemoryMB;
        int    cpuCnt   = SelectedSize.Profile.CpuCount;
        string vmFolder = AppSettings.Current.VmsFolder;

        await RunCreationAsync(names,
            (name, progress, ct) => CreateOneAsync(name, memMB, cpuCnt, vmFolder, progress, ct),
            onSingleSuccess: () => VmName = "");
    }

    private Task CreateOneAsync(string name, long memMB, int cpuCnt, string vmFolder,
                                IProgress<string>? progress, CancellationToken ct)
        => UseDifferencingDisk
            ? HvService.CreateVMWithDifferencingDiskAsync(
                name, SelectedBaseVhdx!, SelectedSwitch!,
                memMB, cpuCnt, Generation, vmFolder,
                null, SecureBoot, PxeBoot, progress, ct)
            : HvService.CreateBlankVmAsync(
                name, SelectedSwitch!,
                memMB, cpuCnt, Generation, vmFolder,
                (long)DiskSizeGB, SecureBoot, PxeBoot, progress, ct);
}
