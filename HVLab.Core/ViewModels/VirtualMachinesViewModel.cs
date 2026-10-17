using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class VirtualMachinesViewModel(IHyperVService hvService, IDialogService dialogs) : PageViewModelBase
{
    private readonly IHyperVService _hvService = hvService;
    private readonly IDialogService _dialogs   = dialogs;

    [ObservableProperty] private ObservableCollection<VirtualMachine> virtualMachines = [];
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
            Status = T("VM_Count", vms.Count);
            MarkLoaded();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            if (!silent) IsLoading = false;
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Updates the collection in-place: adds new entries, removes deleted ones,
    /// updates changed ones — avoids a full redraw on every poll.
    /// </summary>
    private void ApplyDiff(List<VirtualMachine> fresh)
    {
        // Remove VMs no longer present
        var toRemove = VirtualMachines
            .Where(v => !fresh.Any(f => f.Name == v.Name))
            .ToList();
        foreach (var v in toRemove) VirtualMachines.Remove(v);

        foreach (var f in fresh)
        {
            var existing = VirtualMachines.FirstOrDefault(v => v.Name == f.Name);
            if (existing is null)
            {
                VirtualMachines.Add(f);
            }
            else if (existing.IsBusy)
            {
                // Keep the optimistic state ("Starting"…) until the action completes
            }
            else if (existing.State != f.State ||
                     existing.Uptime != f.Uptime ||
                     existing.MemoryMB != f.MemoryMB ||
                     existing.VlanInfo != f.VlanInfo)
            {
                existing.State          = f.State;
                existing.Uptime         = f.Uptime;
                existing.MemoryMB       = f.MemoryMB;
                existing.ProcessorCount = f.ProcessorCount;
                existing.SwitchName     = f.SwitchName;
                existing.VlanInfo       = f.VlanInfo;
            }
        }
    }
    // ─── Actions ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand]
    public Task StartVmAsync(VirtualMachine vm)
        => RunVmActionAsync(vm, "Starting", T("VM_Starting", vm.Name), () => _hvService.StartVMAsync(vm.Name));

    [RelayCommand]
    public Task StopVmAsync(VirtualMachine vm)
        => RunVmActionAsync(vm, "Stopping", T("VM_Stopping", vm.Name), () => _hvService.StopVMAsync(vm.Name));

    [RelayCommand]
    public async Task RemoveVmAsync(VirtualMachine vm)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            T("VM_DeleteTitle"), $"{T("VM_DeleteConfirm")} « {vm.Name} » ?", T("VM_BtnDelete"));
        if (confirmed)
            await RunVmActionAsync(vm, null, T("VM_Removing", vm.Name), () => _hvService.RemoveVMAsync(vm.Name));
    }

    [RelayCommand]
    private void ConnectVm(VirtualMachine vm)
    {
        try   { _hvService.ConnectToVMConsole(vm.Name); }
        catch (Exception ex) { ErrorMessage = $"{T("VM_ErrConsole")} {ex.Message}"; }
    }

    /// <summary>
    /// Runs one action per VM at a time: shows the transitional state immediately,
    /// then refreshes the list once the action completes.
    /// </summary>
    private async Task RunVmActionAsync(VirtualMachine vm, string? optimisticState, string status, Func<Task> action)
    {
        if (vm.IsBusy) return;
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
            ErrorMessage = ex.Message;
        }
        finally { vm.IsBusy = false; }
        await RefreshAsync(silent: true);
    }
}
