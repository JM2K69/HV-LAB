using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();
    private readonly VhdxService _vhdxService = new();

    [ObservableProperty] private int runningVmCount;
    [ObservableProperty] private int stoppedVmCount;
    [ObservableProperty] private int switchCount;
    [ObservableProperty] private int baseVhdxCount;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Cliquez sur Actualiser pour charger les données.";
    [ObservableProperty] private bool showHyperVWarning;
    [ObservableProperty] private string baseVhdxFolder = @"C:\HV-LAB\BaseImages";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = "Chargement...";
        ShowHyperVWarning = false;
        try
        {
            var vms = await _hvService.GetVirtualMachinesAsync();
            RunningVmCount = vms.Count(v => v.IsRunning);
            StoppedVmCount = vms.Count(v => !v.IsRunning);

            var switches = await _hvService.GetVirtualSwitchesAsync();
            SwitchCount = switches.Count;

            var images = await _vhdxService.GetBaseVhdxListAsync(BaseVhdxFolder);
            BaseVhdxCount = images.Count;

            Status = $"Actualisé à {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ShowHyperVWarning = true;
            Status = $"Erreur Hyper-V : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
