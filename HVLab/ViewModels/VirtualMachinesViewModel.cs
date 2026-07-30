using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class VirtualMachinesViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();
    private CancellationTokenSource? _autoRefreshCts;

    [ObservableProperty] private ObservableCollection<VirtualMachine> virtualMachines = [];
    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = "Pret";

    // ─── Refresh ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var vms = await _hvService.GetVirtualMachinesAsync();
            ApplyDiff(vms);
            Status = $"{vms.Count} VM(s)";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
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



    // ─── Auto-refresh ────────────────────────────────────────────────────────

    public void StartAutoRefresh(TimeSpan interval)
    {
        StopAutoRefresh();
        _autoRefreshCts = new CancellationTokenSource();
        var token = _autoRefreshCts.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try { await RefreshAsync(); }
                catch (OperationCanceledException) { break; }
                catch { /* swallow poll errors */ }
            }
        }, token);
    }

    public void StopAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
    }

    // ─── Actions ─────────────────────────────────────────────────────────────

    public async Task StartVmAsync(VirtualMachine vm)
    {
        Status = $"Demarrage de '{vm.Name}'...";
        try
        {
            await _hvService.StartVMAsync(vm.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
    }

    public async Task StopVmAsync(VirtualMachine vm)
    {
        Status = $"Arret de '{vm.Name}'...";
        try
        {
            await _hvService.StopVMAsync(vm.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
    }

    public async Task RemoveVmAsync(VirtualMachine vm)
    {
        Status = $"Suppression de '{vm.Name}'...";
        try
        {
            await _hvService.RemoveVMAsync(vm.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
    }
}
