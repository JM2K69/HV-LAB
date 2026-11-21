using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

/// <summary>
/// Shared state and flow of the VM creation pages (Create VM, Quick VM): switch and base image
/// lists, single / bulk naming, validation of names, cancellable creation loop with progress.
/// </summary>
public abstract partial class VmCreationViewModelBase : PageViewModelBase
{
    protected readonly IHyperVService HvService;
    protected readonly IVhdxService   VhdxService;
    private CancellationTokenSource?  _cts;

    protected VmCreationViewModelBase(IHyperVService hvService, IVhdxService vhdxService)
    {
        HvService   = hvService;
        VhdxService = vhdxService;
    }

    // ── Lists & status ──────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<string> virtualSwitches = [];
    [ObservableProperty] private ObservableCollection<string> baseVhdxList    = [];
    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private bool   isCreating;
    [ObservableProperty] private string status = T("St_Ready");

    /// <summary>Result of the last creation, shown in a Success InfoBar (errors go to the Error InfoBar).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSuccess))] private string successMessage = "";
    public bool HasSuccess => SuccessMessage.Length > 0;
    /// <summary>Name of the VM a single creation made: the "Open the VM" button of the Success InfoBar.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanOpenCreatedVm))] private string createdVmName = "";
    public bool CanOpenCreatedVm => CreatedVmName.Length > 0;

    /// <summary>A form error: in the Error InfoBar, never in the status line.</summary>
    protected void Fail(string message)
    {
        SuccessMessage = "";
        ShowActionError(message);
    }

    [RelayCommand]
    private void DismissSuccess() { SuccessMessage = ""; CreatedVmName = ""; }

    // ── Identity ────────────────────────────────────────────────────────────

    [ObservableProperty] private string  vmName = "";
    [ObservableProperty] private string? selectedSwitch;
    [ObservableProperty] private string? selectedBaseVhdx;

    partial void OnVmNameChanged(string value)            => OnVmNameChangedCore(value);
    partial void OnSelectedBaseVhdxChanged(string? value) => OnSelectedBaseVhdxChangedCore(value);
    protected virtual void OnVmNameChangedCore(string value) { }
    protected virtual void OnSelectedBaseVhdxChangedCore(string? value) { }
    /// <summary>After each (re)load of the switches and bases, e.g. on every visit of the page.</summary>
    protected virtual void OnDataLoadedCore() { }

    // ── Bulk mode ───────────────────────────────────────────────────────────

    [ObservableProperty, NotifyPropertyChangedFor(nameof(BulkNamesPreview))] private bool   bulkMode;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BulkNamesPreview))] private string bulkPrefix = "SRV";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BulkNamesPreview))] private int    bulkCount  = 2;

    /// <summary>Puts the VMs of a series in a Hyper-V VM group named after the prefix.</summary>
    [ObservableProperty] private bool bulkGroup = true;

    partial void OnBulkModeChanged(bool value) => OnBulkModeChangedCore(value);
    protected virtual void OnBulkModeChangedCore(bool value) { }

    public string BulkNamesPreview => FormatBulkPreview(BulkPrefix, BulkCount);

    /// <summary>PREFIX-01 … PREFIX-nn.</summary>
    public static IReadOnlyList<string> BulkNames(string prefix, int count)
        => Enumerable.Range(1, Math.Max(count, 0)).Select(i => $"{prefix}-{i:D2}").ToList();

    /// <summary>First 10 bulk names, then "… (+n)".</summary>
    public static string FormatBulkPreview(string prefix, int count)
    {
        if (string.IsNullOrWhiteSpace(prefix) || count < 1) return "";
        var shown = string.Join("  ", BulkNames(prefix, Math.Min(count, 10)));
        return count > 10 ? $"{shown}  … (+{count - 10})" : shown;
    }

    // ── Loading ─────────────────────────────────────────────────────────────

    /// <summary>Folder scanned for BASE_*.vhdx images.</summary>
    protected abstract string ImagesFolder { get; }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        Status = T("St_Loading");
        try
        {
            // Keep the user's selections: the page is cached and reloads on every visit
            var prevSwitch = SelectedSwitch;
            var prevVhdx   = SelectedBaseVhdx;

            var swTask  = HvService.GetVirtualSwitchesAsync();
            var imgTask = VhdxService.GetBaseVhdxListAsync(ImagesFolder);
            await Task.WhenAll(swTask, imgTask);

            VirtualSwitches.Clear();
            foreach (var sw in swTask.Result) VirtualSwitches.Add(sw.Name);
            BaseVhdxList.Clear();
            foreach (var img in imgTask.Result) BaseVhdxList.Add(img.FilePath);

            if (prevSwitch is not null && VirtualSwitches.Contains(prevSwitch)) SelectedSwitch   = prevSwitch;
            if (prevVhdx   is not null && BaseVhdxList.Contains(prevVhdx))     SelectedBaseVhdx = prevVhdx;
            OnDataLoadedCore();

            Status = T("St_Ready");
            MarkLoaded();
        }
        catch (Exception ex) { Status = T("St_Ready"); ShowError(ex); }
        finally { IsLoading = false; }
    }

    // ── Creation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Names to create for the current mode (one VM, or the bulk series), or null after setting
    /// <see cref="Status"/> to the first validation error.
    /// </summary>
    protected IReadOnlyList<string>? GetNamesToCreate()
    {
        IReadOnlyList<string> names;
        if (BulkMode)
        {
            if (string.IsNullOrWhiteSpace(BulkPrefix)) { Fail(T("St_EnterPrefix")); return null; }
            if (BulkCount < 1)                         { Fail(T("St_CountMin"));    return null; }
            names = BulkNames(BulkPrefix, BulkCount);
        }
        else names = [VmName];

        foreach (var name in names)
            if (InputValidator.CheckVmName(name) is { } key) { Fail($"{T(key)} ({name})"); return null; }
        return names;
    }

    /// <summary>
    /// Creates the VMs one after the other. A single VM reports script progress in
    /// <see cref="Status"/>; a series reports "i/n" and keeps going after a failure.
    /// Cancel stops the current VM and the series.
    /// </summary>
    protected async Task RunCreationAsync(
        IReadOnlyList<string> names,
        Func<string, IProgress<string>?, CancellationToken, Task> createOne,
        Action? onSingleSuccess = null)
    {
        IsLoading = true; IsCreating = true;
        ClearActionError();
        SuccessMessage = ""; CreatedVmName = "";
        var cts     = _cts = new CancellationTokenSource();
        var created = new List<string>();
        var errors  = new List<string>();
        var bulk    = names.Count > 1 || BulkMode;
        try
        {
            if (!bulk)
            {
                var name = names[0];
                Status = T("St_CreatingVm", name);
                await createOne(name, new Progress<string>(line => Status = TranslateProgress(line)), cts.Token);
                Status = T("St_Ready");
                SuccessMessage = T("St_VmCreated", name);
                CreatedVmName = name;
                onSingleSuccess?.Invoke();
                return;
            }

            for (int i = 0; i < names.Count; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                Status = T("St_CreatingN", i + 1, names.Count, names[i]);
                try
                {
                    await createOne(names[i], null, cts.Token);
                    created.Add(names[i]);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { errors.Add($"{names[i]}: {ex.Message}"); }
            }
            if (BulkGroup && BulkMode && created.Count > 0)
            {
                try { await HvService.AddToGroupAsync(BulkPrefix, created); }
                catch (Exception ex) { errors.Add(T("St_GroupFailed", BulkPrefix, ex.Message)); }
            }
            Status = T("St_Ready");
            if (created.Count > 0) SuccessMessage = T("St_BulkOk", created.Count, string.Join(", ", created));
            // One line per failed VM, in the Error InfoBar
            if (errors.Count > 0)
                ShowActionError(T("St_BulkPartial", created.Count, errors.Count, "").TrimEnd(' ', ':', '|')
                                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
        catch (OperationCanceledException)
        {
            Status = bulk ? T("Op_CancelledBulk", created.Count, names.Count) : T("Op_Cancelled");
        }
        catch (Exception ex) { Status = T("St_Ready"); ShowActionError(ex); }
        finally
        {
            IsLoading = false; IsCreating = false;
            cts.Dispose(); _cts = null;
        }
    }
}
