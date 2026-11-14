using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

/// <summary>
/// The VM list: live rows, per-row actions, group filter and bulk actions on the selection.
/// The details of one VM (screen, credentials, checkpoints, settings) live in its own window (<see cref="VmDetailsViewModel"/>).
/// </summary>
public partial class VirtualMachinesViewModel(IHyperVService hvService, IDialogService dialogs) : PageViewModelBase
{
    private readonly IHyperVService _hvService = hvService;
    private readonly IDialogService _dialogs   = dialogs;

    /// <summary>Every VM of the host, updated in place on each refresh.</summary>
    [ObservableProperty] private ObservableCollection<VirtualMachine> virtualMachines = [];
    /// <summary>The VMs shown in the list: <see cref="VirtualMachines"/> filtered by the group.</summary>
    public ObservableCollection<VirtualMachine> VisibleMachines { get; } = [];

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = T("St_Ready");

    /// <summary>True once a successful refresh returned no VM: shows the empty state.</summary>
    public bool IsEmpty => IsEmptyAfterLoad(VirtualMachines.Count);

    protected override void OnEmptyStateChanged() => OnPropertyChanged(nameof(IsEmpty));

    // Serializes refreshes: a poll tick never overlaps a manual refresh.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // ─── Refresh ────────────────────────────────────────────────────────────
    /// <param name="silent">Background poll: no loading indicator, skipped if a refresh is running.</param>
    public async Task RefreshAsync(bool silent = false)
    {
        if (!await _refreshGate.WaitAsync(silent ? 0 : Timeout.Infinite)) return;
        if (!silent) IsLoading = true;
        try
        {
            var vms = await _hvService.GetVirtualMachinesAsync();
            ApplyDiff(vms);
            RefreshGroups();
            SyncVisible();
            Status = T("VM_Count", vms.Count);
            MarkLoaded();
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            if (!silent) IsLoading = false;
            _refreshGate.Release();
        }
    }

    /// <summary>Key of a row: the Hyper-V GUID (stable across renames), else the name.</summary>
    private static string Key(VirtualMachine v) => v.Id.Length > 0 ? v.Id : v.Name;

    /// <summary>
    /// Updates the collection in-place: adds new entries, removes deleted ones,
    /// updates changed ones — avoids a full redraw on every poll.
    /// </summary>
    private void ApplyDiff(List<VirtualMachine> fresh)
    {
        var freshKeys = fresh.Select(Key).ToHashSet();
        foreach (var v in VirtualMachines.Where(v => !freshKeys.Contains(Key(v))).ToList())
            VirtualMachines.Remove(v);

        foreach (var f in fresh)
        {
            var existing = VirtualMachines.FirstOrDefault(v => Key(v) == Key(f));
            if (existing is null)          VirtualMachines.Add(f);
            else if (!existing.IsBusy)     existing.UpdateFrom(f);   // busy: keep the optimistic state until the action ends
        }
    }

    // ─── Group filter ───────────────────────────────────────────────────────

    /// <summary>"All groups" followed by the Hyper-V VM groups of the host.</summary>
    public ObservableCollection<string> Groups { get; } = [T("VM_GroupAll")];
    [ObservableProperty] private string? selectedGroup;

    private bool ShowsAllGroups => string.IsNullOrEmpty(SelectedGroup) || SelectedGroup == Groups[0];

    partial void OnSelectedGroupChanged(string? value) => SyncVisible();

    private void RefreshGroups()
    {
        var names = VirtualMachines.Select(v => v.GroupName).Where(g => g.Length > 0).Distinct().Order().ToList();
        foreach (var g in Groups.Skip(1).Where(g => !names.Contains(g)).ToList()) Groups.Remove(g);
        foreach (var g in names.Where(g => !Groups.Contains(g))) Groups.Add(g);
        if (!ShowsAllGroups && !Groups.Contains(SelectedGroup!)) SelectedGroup = Groups[0];
    }

    /// <summary>Brings <see cref="VisibleMachines"/> in line with the filter, keeping row instances (and the selection).</summary>
    private void SyncVisible()
    {
        var wanted = VirtualMachines.Where(v => ShowsAllGroups || v.GroupName == SelectedGroup).ToList();
        for (int i = VisibleMachines.Count - 1; i >= 0; i--)
            if (!wanted.Contains(VisibleMachines[i])) VisibleMachines.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < VisibleMachines.Count && ReferenceEquals(VisibleMachines[i], wanted[i])) continue;
            var at = VisibleMachines.IndexOf(wanted[i]);
            if (at >= 0) VisibleMachines.Move(at, i); else VisibleMachines.Insert(i, wanted[i]);
        }
    }

    // ─── Selection (bulk bar for several VMs) ───────────────────────────────

    private List<VirtualMachine> _selection = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMultiSelection), nameof(SelectionText))]
    private int selectionCount;

    public bool   HasMultiSelection => SelectionCount > 1;
    public string SelectionText     => T("VM_SelectionCount", SelectionCount);

    /// <summary>Called by the view when the list selection changes.</summary>
    public void SetSelection(IEnumerable<VirtualMachine> items)
    {
        _selection = items.ToList();
        SelectionCount = _selection.Count;
    }

    // ─── Single-VM actions ──────────────────────────────────────────────────

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand] public Task StartVmAsync(VirtualMachine vm)   => PowerAsync(vm, VmPowerAction.Start,    "Starting", "VM_Starting");
    [RelayCommand] public Task StopVmAsync(VirtualMachine vm)    => PowerAsync(vm, VmPowerAction.Shutdown, "Stopping", "VM_Stopping");
    [RelayCommand] public Task TurnOffVmAsync(VirtualMachine vm) => PowerAsync(vm, VmPowerAction.TurnOff,  "Stopping", "VM_TurningOff");
    [RelayCommand] public Task SaveVmAsync(VirtualMachine vm)    => PowerAsync(vm, VmPowerAction.Save,     "Saving",   "VM_Saving");
    [RelayCommand] public Task PauseVmAsync(VirtualMachine vm)   => PowerAsync(vm, VmPowerAction.Pause,    "Pausing",  "VM_Pausing");
    [RelayCommand] public Task ResumeVmAsync(VirtualMachine vm)  => PowerAsync(vm, VmPowerAction.Resume,   "Resuming", "VM_Resuming");
    [RelayCommand] public Task ResetVmAsync(VirtualMachine vm)   => PowerAsync(vm, VmPowerAction.Reset,    null,       "VM_Resetting");
    [RelayCommand] public Task RestartVmAsync(VirtualMachine vm) => PowerAsync(vm, VmPowerAction.Restart,  null,       "VM_Restarting");

    private Task PowerAsync(VirtualMachine vm, VmPowerAction action, string? optimisticState, string statusKey)
        => RunVmActionAsync(vm, optimisticState, T(statusKey, vm.Name), () => _hvService.PowerAsync(vm.Id, action));

    [RelayCommand]
    public async Task RemoveVmAsync(VirtualMachine vm)
    {
        var plan = await TryDeletePlanAsync(vm);
        var content = T("VM_DeleteConfirmText", vm.Name) + FilesText(plan is null ? [] : [plan]);
        var (confirmed, withFiles) = await _dialogs.ConfirmWithOptionAsync(T("VM_DeleteTitle"), content, T("VM_DeleteConfirm"),
            plan?.Files.Count > 0 ? T("VM_DeleteWithDisks") : null, true);
        if (confirmed)
            await RunVmActionAsync(vm, null, T("VM_Removing", vm.Name),
                () => _hvService.RemoveVMAsync(vm.Target, withFiles ? plan?.Files : null));
    }

    /// <summary>Disk plan of a delete; null when it cannot be computed (the VM is then deleted without its files).</summary>
    private async Task<VmDeletePlan?> TryDeletePlanAsync(VirtualMachine vm)
    {
        try { return await _hvService.GetDeletePlanAsync(vm.Target); }
        catch (Exception ex) { CrashLogger.Log($"Delete plan of {vm.Name}", ex); return null; }
    }

    /// <summary>Files listed in the delete confirmation (at most 12), plus the shared parents that are kept.</summary>
    private static string FilesText(IReadOnlyList<VmDeletePlan> plans)
    {
        var files = plans.SelectMany(p => p.Files).ToList();
        var kept  = plans.SelectMany(p => p.KeptParents).Distinct().ToList();
        var text = "";
        if (files.Count > 0)
            text += "\n\n" + T("VM_DeleteFiles", files.Count) + "\n" +
                    string.Join("\n", files.Take(12).Select(f => "• " + Path.GetFileName(f))) + (files.Count > 12 ? "\n…" : "");
        if (kept.Count > 0)
            text += "\n\n" + T("VM_DeleteKept", string.Join(", ", kept.Select(Path.GetFileName)));
        return text;
    }

    [RelayCommand]
    private void ConnectVm(VirtualMachine vm)
    {
        try   { _hvService.ConnectToVMConsole(vm.Target); }
        catch (Exception ex) { ShowActionError(ex, T("VM_ErrConsole")); }
    }

    /// <summary>
    /// Runs one action per VM at a time: shows the transitional state immediately,
    /// then refreshes the list once the action completes.
    /// </summary>
    private async Task RunVmActionAsync(VirtualMachine vm, string? optimisticState, string status, Func<Task> action,
                                        bool refresh = true)
    {
        if (vm.IsBusy) return;
        if (refresh) ClearActionError();   // bulk actions clear it once, before the first VM
        vm.IsBusy = true;
        var previousState = vm.State;
        if (optimisticState is not null) vm.State = optimisticState;
        Status = status;
        try
        {
            await action();
            Status = T("VM_ActionOk", vm.Name);
        }
        catch (Exception ex)
        {
            vm.State = previousState;
            ShowActionError(ex);
        }
        finally { vm.IsBusy = false; }
        if (refresh) await RefreshAsync(silent: true);
    }

    // ─── Bulk actions on the selection ──────────────────────────────────────

    [RelayCommand] private Task StartSelectedAsync()   => BulkPowerAsync(VmPowerAction.Start,    "Starting", v => v.CanStart);
    [RelayCommand] private Task StopSelectedAsync()    => BulkPowerAsync(VmPowerAction.Shutdown, "Stopping", v => v.CanStop);
    [RelayCommand] private Task TurnOffSelectedAsync() => BulkPowerAsync(VmPowerAction.TurnOff,  "Stopping", v => v.CanTurnOff);

    /// <summary>Same action on every selected VM where it applies, in parallel (one action per VM at a time).</summary>
    private async Task BulkPowerAsync(VmPowerAction action, string optimisticState, Func<VirtualMachine, bool> applies)
    {
        var targets = _selection.Where(applies).ToList();
        if (targets.Count == 0) return;
        ClearActionError();
        await Task.WhenAll(targets.Select(v => RunVmActionAsync(v, optimisticState, T("VM_BulkAction", targets.Count),
            () => _hvService.PowerAsync(v.Id, action), refresh: false)));
        await RefreshAsync(silent: true);
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var targets = _selection.Where(v => v.CanRemove).ToList();
        if (targets.Count == 0) { Status = T("VM_BulkNothingToDelete"); return; }
        var plans = new Dictionary<VirtualMachine, VmDeletePlan?>();
        foreach (var v in targets) plans[v] = await TryDeletePlanAsync(v);
        var known = plans.Values.OfType<VmDeletePlan>().ToList();
        var content = T("VM_BulkDeleteText", targets.Count, string.Join(", ", targets.Select(v => v.Name))) + FilesText(known);
        var (confirmed, withFiles) = await _dialogs.ConfirmWithOptionAsync(T("VM_DeleteTitle"), content, T("VM_DeleteConfirm"),
            known.Any(p => p.Files.Count > 0) ? T("VM_DeleteWithDisks") : null, true);
        if (!confirmed) return;
        ClearActionError();
        foreach (var v in targets)
            await RunVmActionAsync(v, null, T("VM_Removing", v.Name),
                () => _hvService.RemoveVMAsync(v.Target, withFiles ? plans[v]?.Files : null), refresh: false);
        await RefreshAsync(silent: true);
    }

    [RelayCommand]
    private async Task GroupSelectedAsync()
    {
        if (_selection.Count == 0) return;
        var suggested = _selection.Select(v => v.GroupName).FirstOrDefault(g => g.Length > 0) ?? CommonPrefix(_selection);
        var name = await _dialogs.PromptAsync(T("VM_GroupTitle"), T("VM_GroupName"), suggested);
        if (name is null) return;
        await RunAsync(T("VM_Grouping", name), () => _hvService.AddToGroupAsync(name, _selection.Select(v => v.Target).ToList()));
    }

    [RelayCommand]
    private Task UngroupSelectedAsync()
        => _selection.Count == 0 ? Task.CompletedTask
         : RunAsync(T("VM_Ungrouping"), () => _hvService.RemoveFromGroupsAsync(_selection.Select(v => v.Target).ToList()));

    /// <summary>"SRV-01", "SRV-02" → "SRV": the bulk-creation prefix, as a group name suggestion.</summary>
    internal static string CommonPrefix(IReadOnlyList<VirtualMachine> vms)
    {
        if (vms.Count == 0) return "";
        var prefix = vms[0].Name;
        foreach (var v in vms.Skip(1))
            while (!v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) prefix = prefix[..^1];
        return prefix.TrimEnd('-', '_', ' ', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9') is { Length: > 0 } p ? p : vms[0].Name;
    }

    /// <summary>Non-VM action (group…): status, error banner, then a silent refresh.</summary>
    private async Task RunAsync(string status, Func<Task> action)
    {
        ClearActionError();
        Status = status;
        try { await action(); Status = T("St_Ready"); }
        catch (Exception ex) { ShowActionError(ex); }
        await RefreshAsync(silent: true);
    }
}
