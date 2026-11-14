using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class DashboardViewModel(IHyperVService hvService, IVhdxService vhdxService, AppStartupCache startupCache)
    : ObservableObject
{
    private readonly IHyperVService _hvService   = hvService;
    private readonly IVhdxService   _vhdxService = vhdxService;

    [ObservableProperty] private int runningVmCount;
    [ObservableProperty] private int stoppedVmCount;
    [ObservableProperty] private int switchCount;
    [ObservableProperty] private int baseVhdxCount;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = T("Dash_ClickRefresh");
    [ObservableProperty] private bool showHyperVWarning;
    [ObservableProperty] private string baseVhdxFolder = AppSettings.Current.BaseImagesFolder;

    // ── Host capacity: what the running VMs use, against what the host has
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCapacity))] private string memoryText = "";
    [ObservableProperty] private string cpuText = "";
    [ObservableProperty] private string diskText = "";
    public bool HasCapacity => MemoryText.Length > 0;

    /// <summary>The three capacity lines of the dashboard (memory, processors, drives).</summary>
    public static (string Memory, string Cpu, string Disk) CapacityLines(HostCapacity host, IReadOnlyCollection<HVLab.Models.VirtualMachine> vms)
    {
        var running = vms.Where(v => v.IsRunning).ToList();
        var memory = T("Dash_Memory", Gb(running.Sum(v => v.MemoryMB)), Gb(host.FreeMemoryMB), Gb(host.TotalMemoryMB));
        var cpu    = T("Dash_Cpu", running.Sum(v => v.ProcessorCount), host.LogicalProcessors);
        var disk   = string.Join(" · ", host.Drives.Select(d => T("Dash_Drive", d.Drive, d.FreeGB, d.TotalGB)));
        return (memory, cpu, disk);
    }

    private static string Gb(long mb) => (mb / 1024.0).ToString("0.#");

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
            if (startupCache.TryConsume(out vms, out switches, out images, out var startupError))
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

            // Capacity is a bonus: the dashboard still shows the counts when it cannot be read
            try { (MemoryText, CpuText, DiskText) = CapacityLines(await _hvService.GetHostCapacityAsync(), vms); }
            catch (Exception) { (MemoryText, CpuText, DiskText) = ("", "", ""); }

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
