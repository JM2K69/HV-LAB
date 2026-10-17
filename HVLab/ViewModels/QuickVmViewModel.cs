using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class QuickVmViewModel : ObservableObject
{
    private readonly HyperVService _hvService   = new();
    private readonly VhdxService   _vhdxService = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool isCreating;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

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
            // Keep the user's selections: the page is cached and reloads on every visit
            var prevSwitch = SelectedSwitch;
            var prevVhdx   = SelectedBaseVhdx;

            var swTask  = _hvService.GetVirtualSwitchesAsync();
            var imgTask = _vhdxService.GetBaseVhdxListAsync(AppSettings.Current.BaseImagesFolder);
            await Task.WhenAll(swTask, imgTask);

            var switches = swTask.Result;
            VirtualSwitches.Clear();
            foreach (var sw in switches) VirtualSwitches.Add(sw.Name);

            var images = imgTask.Result;
            BaseVhdxList.Clear();
            foreach (var img in images) BaseVhdxList.Add(img.FilePath);

            if (prevSwitch is not null && VirtualSwitches.Contains(prevSwitch)) SelectedSwitch   = prevSwitch;
            if (prevVhdx   is not null && BaseVhdxList.Contains(prevVhdx))     SelectedBaseVhdx = prevVhdx;
        }
        catch (Exception ex) { Status = T("St_LoadError", ex.Message); }
        finally { IsLoading = false; }
    }

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

        long   memMB    = SelectedSize.Profile.MemoryMB;
        int    cpuCnt   = SelectedSize.Profile.CpuCount;
        string vmFolder = AppSettings.Current.VmsFolder;

        if (BulkMode)
        {
            if (string.IsNullOrWhiteSpace(BulkPrefix)) { Status = T("St_EnterPrefix"); return; }
            if (BulkCount < 1)                         { Status = T("St_CountMin"); return; }

            var names = Enumerable.Range(1, BulkCount).Select(i => $"{BulkPrefix}-{i:D2}").ToList();
            if (names.Select(InputValidator.CheckVmName).FirstOrDefault(k => k is not null) is { } bad)
                { Status = LocalizationService.Instance[bad]; return; }

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
                    Status = T("St_CreatingN", i + 1, names.Count, name);
                    try
                    {
                        await CreateOneAsync(name, memMB, cpuCnt, vmFolder, null, _cts.Token);
                        created.Add(name);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{name}: {ex.Message}"); }
                }

                Status = errors.Count == 0
                    ? T("St_BulkOk", created.Count, string.Join(", ", created))
                    : T("St_BulkPartial", created.Count, errors.Count, string.Join(" | ", errors));
            }
            catch (OperationCanceledException)
            {
                Status = string.Format(LocalizationService.Instance["Op_CancelledBulk"], created.Count, names.Count);
            }
            finally { IsLoading = false; IsCreating = false; _cts.Dispose(); _cts = null; }
        }
        else
        {
            if (InputValidator.CheckVmName(VmName) is { } bad)
                { Status = LocalizationService.Instance[bad]; return; }

            IsLoading = true; IsCreating = true;
            _cts = new CancellationTokenSource();
            Status = T("St_CreatingVm", VmName);
            try
            {
                await CreateOneAsync(VmName, memMB, cpuCnt, vmFolder,
                                     new Progress<string>(line => Status = TranslateProgress(line)), _cts.Token);

                Status = T("St_VmCreated", VmName);
                VmName = "";
            }
            catch (OperationCanceledException) { Status = LocalizationService.Instance["Op_Cancelled"]; }
            catch (Exception ex) { Status = T("St_Error", ex.Message); }
            finally { IsLoading = false; IsCreating = false; _cts.Dispose(); _cts = null; }
        }
    }

    private Task CreateOneAsync(string name, long memMB, int cpuCnt, string vmFolder,
                                IProgress<string>? progress, CancellationToken ct)
        => UseDifferencingDisk
            ? _hvService.CreateVMWithDifferencingDiskAsync(
                name, SelectedBaseVhdx!, SelectedSwitch!,
                memMB, cpuCnt, Generation, vmFolder,
                null, SecureBoot, PxeBoot, progress, ct)
            : _hvService.CreateBlankVmAsync(
                name, SelectedSwitch!,
                memMB, cpuCnt, Generation, vmFolder,
                (long)DiskSizeGB, SecureBoot, PxeBoot, progress, ct);
}
