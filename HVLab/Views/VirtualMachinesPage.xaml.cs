using HVLab.Models;
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

    // WMI makes a refresh cheap: the list polls every 3 s while the page is visible
    private readonly DispatcherQueueTimer _pollTimer;

    public VirtualMachinesPage()
    {
        InitializeComponent();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
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

    private void VmList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SetSelection(VmList.SelectedItems.OfType<VirtualMachine>());

    // ── The VM's own window: double-click, Enter, the details button, or "Settings…" in the menu

    private void VmList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is VirtualMachine vm) VmWindow.Show(vm);
    }

    private void OpenSelected_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (VmList.SelectedItems.Count == 1 && VmList.SelectedItem is VirtualMachine vm)
        {
            VmWindow.Show(vm);
            args.Handled = true;
        }
    }

    // Delete key: the selected VM, or every selected VM (each with confirmation)
    private void DeleteSelected_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (VmList.SelectedItems.Count > 1) { ViewModel.RemoveSelectedCommand.Execute(null); args.Handled = true; }
        else if (VmList.SelectedItem is VirtualMachine vm && ViewModel.RemoveVmCommand.CanExecute(vm))
        {
            ViewModel.RemoveVmCommand.Execute(vm);
            args.Handled = true;
        }
    }

    // ── Row buttons and "More actions" menu: the row's VM is in the Tag (flyouts do not see the page by ElementName)
    private static VirtualMachine? VmOf(object sender) => (sender as FrameworkElement)?.Tag as VirtualMachine;

    private void Details_Click(object sender, RoutedEventArgs e)  { if (VmOf(sender) is { } vm) VmWindow.Show(vm); }

    /// <summary>Error banner: the message and PowerShell's output, ready to paste into an issue.</summary>
    private void CopyErrorDetails_Click(object sender, RoutedEventArgs e)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText($"{ViewModel.ErrorMessage}\n\n{ViewModel.ErrorDetails}");
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }
    private void Settings_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) VmWindow.Show(vm, VmWindowTab.Settings); }
    private void ConsoleTab_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) VmWindow.Show(vm, VmWindowTab.Console); }
    private void TurnOff_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ViewModel.TurnOffVmCommand.Execute(vm); }
    private void Restart_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ViewModel.RestartVmCommand.Execute(vm); }
    private void Reset_Click(object sender, RoutedEventArgs e)   { if (VmOf(sender) is { } vm) ViewModel.ResetVmCommand.Execute(vm); }
    private void Save_Click(object sender, RoutedEventArgs e)    { if (VmOf(sender) is { } vm) ViewModel.SaveVmCommand.Execute(vm); }
    private void Pause_Click(object sender, RoutedEventArgs e)   { if (VmOf(sender) is { } vm) ViewModel.PauseVmCommand.Execute(vm); }
    private void Resume_Click(object sender, RoutedEventArgs e)  { if (VmOf(sender) is { } vm) ViewModel.ResumeVmCommand.Execute(vm); }
    private void Delete_Click(object sender, RoutedEventArgs e)  { if (VmOf(sender) is { } vm) ViewModel.RemoveVmCommand.Execute(vm); }

    // Navigation is a view concern: stays in code-behind
    private void CreateVm_Click(object sender, RoutedEventArgs e)
        => App.MainAppWindow?.NavigateTo("quickvm");
}
