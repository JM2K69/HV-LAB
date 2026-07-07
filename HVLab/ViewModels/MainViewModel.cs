using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HyperVService _hyperVService;

    [ObservableProperty]
    private ObservableCollection<VirtualSwitch> virtualSwitches = new();

    [ObservableProperty]
    private ObservableCollection<VirtualMachine> virtualMachines = new();

    [ObservableProperty]
    private ObservableCollection<BaseVhdx> baseVhdxImages = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string statusMessage = "Ready";

    public MainViewModel()
    {
        _hyperVService = new HyperVService();
    }

    [RelayCommand]
    public async Task RefreshVirtualSwitches()
    {
        IsLoading = true;
        StatusMessage = "Loading virtual switches...";
        try
        {
            var switches = await _hyperVService.GetVirtualSwitchesAsync();
            VirtualSwitches.Clear();
            foreach (var sw in switches)
                VirtualSwitches.Add(sw);
            StatusMessage = $"Loaded {switches.Count} virtual switches";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RefreshVirtualMachines()
    {
        IsLoading = true;
        StatusMessage = "Loading virtual machines...";
        try
        {
            var vms = await _hyperVService.GetVirtualMachinesAsync();
            VirtualMachines.Clear();
            foreach (var vm in vms)
                VirtualMachines.Add(vm);
            StatusMessage = $"Loaded {vms.Count} virtual machines";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateVirtualSwitch()
    {
        IsLoading = true;
        StatusMessage = "Creating virtual switch...";
        try
        {
            // TODO: Show dialog for switch details
            await _hyperVService.CreateVSwitchNATAsync("NewSwitch", "192.168.100.1", "255.255.255.0");
            await RefreshVirtualSwitchesCommand.ExecuteAsync(null);
            StatusMessage = "Virtual switch created successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateVirtualMachine()
    {
        IsLoading = true;
        StatusMessage = "Creating virtual machine...";
        try
        {
            // TODO: Show dialog for VM details
            await _hyperVService.CreateVirtualMachineAsync("NewVM", 2048, 2, "Default");
            await RefreshVirtualMachinesCommand.ExecuteAsync(null);
            StatusMessage = "Virtual machine created successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StartVM(VirtualMachine? vm)
    {
        if (vm == null) return;
        IsLoading = true;
        StatusMessage = $"Starting {vm.Name}...";
        try
        {
            await _hyperVService.StartVMAsync(vm.Name);
            StatusMessage = $"{vm.Name} started successfully";
            await RefreshVirtualMachinesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task StopVM(VirtualMachine? vm)
    {
        if (vm == null) return;
        IsLoading = true;
        StatusMessage = $"Stopping {vm.Name}...";
        try
        {
            await _hyperVService.StopVMAsync(vm.Name);
            StatusMessage = $"{vm.Name} stopped successfully";
            await RefreshVirtualMachinesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RemoveVM(VirtualMachine? vm)
    {
        if (vm == null) return;
        IsLoading = true;
        StatusMessage = $"Removing {vm.Name}...";
        try
        {
            await _hyperVService.RemoveVMAsync(vm.Name);
            StatusMessage = $"{vm.Name} removed successfully";
            await RefreshVirtualMachinesCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
