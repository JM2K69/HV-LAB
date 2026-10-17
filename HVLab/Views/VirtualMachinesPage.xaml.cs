using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualMachinesPage : Page
{
    public VirtualMachinesViewModel ViewModel { get; } = App.GetService<VirtualMachinesViewModel>();

    /// <summary>Gen / CPU / VLAN columns: collapsed when the page is narrower than 900 px.</summary>
    public Visibility DetailColumnsVisibility
    {
        get => (Visibility)GetValue(DetailColumnsVisibilityProperty);
        set => SetValue(DetailColumnsVisibilityProperty, value);
    }

    public static readonly DependencyProperty DetailColumnsVisibilityProperty =
        DependencyProperty.Register(nameof(DetailColumnsVisibility), typeof(Visibility),
                                    typeof(VirtualMachinesPage), new PropertyMetadata(Visibility.Visible));

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => DetailColumnsVisibility = e.NewSize.Width < 900 ? Visibility.Collapsed : Visibility.Visible;

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

    // Delete key on the focused list removes the selected VM (with confirmation)
    private void DeleteSelected_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (VmList.SelectedItem is HVLab.Models.VirtualMachine vm && ViewModel.RemoveVmCommand.CanExecute(vm))
        {
            ViewModel.RemoveVmCommand.Execute(vm);
            args.Handled = true;
        }
    }

    // Navigation is a view concern: stays in code-behind
    private void CreateVm_Click(object sender, RoutedEventArgs e)
        => App.MainAppWindow?.NavigateTo("quickvm");
}
