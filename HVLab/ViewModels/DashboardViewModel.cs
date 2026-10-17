using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

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
    [ObservableProperty] private string status = T("Dash_ClickRefresh");
    [ObservableProperty] private bool showHyperVWarning;
    [ObservableProperty] private string baseVhdxFolder = AppSettings.Current.BaseImagesFolder;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = T("St_Loading");
        ShowHyperVWarning = false;
        try
        {
            List<HVLab.Models.VirtualMachine> vms;
            List<HVLab.Models.VirtualSwitch>  switches;
            List<HVLab.Models.BaseVhdx>       images;

            // Les données pré-chargées par le splash ne servent qu'au premier affichage
            if (AppStartupCache.Instance.TryConsume(out vms, out switches, out images, out var startupError))
            {
                if (startupError is not null) throw startupError;
            }
            else
            {
                BaseVhdxFolder = AppSettings.Current.BaseImagesFolder;
                var vmTask   = _hvService.GetVirtualMachinesAsync();
                var swTask   = _hvService.GetVirtualSwitchesAsync();
                var vhdxTask = _vhdxService.GetBaseVhdxListAsync(BaseVhdxFolder);
                await Task.WhenAll(vmTask, swTask, vhdxTask);
                vms      = vmTask.Result;
                switches = swTask.Result;
                images   = vhdxTask.Result;
            }

            RunningVmCount = vms.Count(v => v.IsRunning);
            StoppedVmCount = vms.Count(v => !v.IsRunning);
            SwitchCount    = switches.Count;
            BaseVhdxCount  = images.Count;

            Status = T("Dash_Updated", DateTime.Now);
        }
        catch (Exception ex)
        {
            ShowHyperVWarning = true;
            Status = T("Dash_HvError", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
