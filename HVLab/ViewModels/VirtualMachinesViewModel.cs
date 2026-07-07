using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class VirtualMachinesViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();

    [ObservableProperty] private ObservableCollection<VirtualMachine> virtualMachines = [];
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Prêt";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = "Chargement des machines virtuelles...";
        try
        {
            var vms = await _hvService.GetVirtualMachinesAsync();
            VirtualMachines.Clear();
            foreach (var vm in vms) VirtualMachines.Add(vm);
            Status = $"{vms.Count} VM(s) trouvée(s)";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public async Task StartVmAsync(VirtualMachine vm)
    {
        IsLoading = true;
        Status = $"Démarrage de '{vm.Name}'…";
        try
        {
            await _hvService.StartVMAsync(vm.Name);
            Status = $"VM '{vm.Name}' démarrée";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    public async Task StopVmAsync(VirtualMachine vm)
    {
        IsLoading = true;
        Status = $"Arrêt de '{vm.Name}'…";
        try
        {
            await _hvService.StopVMAsync(vm.Name);
            Status = $"VM '{vm.Name}' arrêtée";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    public async Task RemoveVmAsync(VirtualMachine vm)
    {
        IsLoading = true;
        Status = $"Suppression de '{vm.Name}'…";
        try
        {
            await _hvService.RemoveVMAsync(vm.Name);
            Status = $"VM '{vm.Name}' supprimée";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }
}
