using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualMachinesPage : Page
{
    public VirtualMachinesViewModel ViewModel { get; } = new();
    public LocalizationService Loc => LocalizationService.Instance;

    // Polls VM state while the page is visible (ApplyDiff updates rows in place).
    private readonly DispatcherQueueTimer _pollTimer;

    public VirtualMachinesPage()
    {
        InitializeComponent();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(5);
        _pollTimer.Tick += async (_, _) => await ViewModel.RefreshAsync(silent: true);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
        _pollTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _pollTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RefreshAsync();

    private async void StartVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VirtualMachine vm })
            await ViewModel.StartVmAsync(vm);
    }

    private async void StopVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: VirtualMachine vm })
            await ViewModel.StopVmAsync(vm);
    }

    private void ConnectVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VirtualMachine vm }) return;
        try
        {
            HyperVService.ConnectToVMConsole(vm.Name);
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"{Loc["VM_ErrConsole"]} {ex.Message}";
        }
    }

    private async void DeleteVm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: VirtualMachine vm }) return;
        var loc = LocalizationService.Instance;
        var dialog = new ContentDialog
        {
            Title             = loc["VM_DeleteTitle"],
            Content           = $"{loc["VM_DeleteConfirm"]} \u00AB {vm.Name} \u00BB ?",
            PrimaryButtonText = loc["VM_BtnDelete"],
            CloseButtonText   = loc["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveVmAsync(vm);
    }
}
