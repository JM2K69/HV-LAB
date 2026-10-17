using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualMachinesPage : Page
{
    public VirtualMachinesViewModel ViewModel { get; } = App.GetService<VirtualMachinesViewModel>();

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

    // Navigation is a view concern: stays in code-behind
    private void CreateVm_Click(object sender, RoutedEventArgs e)
        => App.MainAppWindow?.NavigateTo("quickvm");
}
