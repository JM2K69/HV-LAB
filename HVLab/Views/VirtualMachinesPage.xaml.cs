using HVLab.Models;
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

    /// <summary>Gen / CPU / VLAN columns: collapsed when the page is narrower than 1000 px (window at its minimum).</summary>
    public Visibility DetailColumnsVisibility
    {
        get => (Visibility)GetValue(DetailColumnsVisibilityProperty);
        set => SetValue(DetailColumnsVisibilityProperty, value);
    }

    public static readonly DependencyProperty DetailColumnsVisibilityProperty =
        DependencyProperty.Register(nameof(DetailColumnsVisibility), typeof(Visibility),
                                    typeof(VirtualMachinesPage), new PropertyMetadata(Visibility.Visible));

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        => DetailColumnsVisibility = e.NewSize.Width < 1000 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>An exported blueprint opens in the Labs editor (the view model told the page which file).</summary>
    private void OnBlueprintWritten(object? sender, string file) => App.MainAppWindow?.NavigateTo("labs");

    private void Labs_Click(object sender, RoutedEventArgs e) => App.MainAppWindow?.NavigateTo("labs");

    /// <summary>Row menu: one VM (or the VMs selected with it) as a blueprint.</summary>
    private async void ExportBlueprint_Click(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is not { } vm) return;
        var selected = VmList.SelectedItems.OfType<VirtualMachine>().ToList();
        await ViewModel.ExportBlueprintAsync(selected.Contains(vm) && selected.Count > 1 ? selected : [vm]);
    }

    /// <summary>Tags of the row's VM, or of the selection when the row is part of it.</summary>
    private async void Tags_Click(object sender, RoutedEventArgs e)
    {
        if (VmOf(sender) is not { } vm) return;
        var selected = VmList.SelectedItems.OfType<VirtualMachine>().ToList();
        await ViewModel.EditTagsAsync(selected.Contains(vm) && selected.Count > 1 ? selected : [vm]);
    }

    private void Search_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    /// <summary>Right-click (or the menu key) on a row: the row's "More actions" menu, at the pointer.</summary>
    private void Row_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        var more = Descendants(sender).OfType<Button>().LastOrDefault(b => b.Flyout is MenuFlyout);
        if (more?.Flyout is not MenuFlyout menu) return;
        if (e.TryGetPosition(sender, out var point))
            menu.ShowAt(sender, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = point });
        else
            menu.ShowAt(more);
        e.Handled = true;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    // WMI makes a refresh cheap: the list polls every 3 s while the page is visible
    private readonly DispatcherQueueTimer _pollTimer;

    public VirtualMachinesPage()
    {
        InitializeComponent();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
        _pollTimer.Tick += async (_, _) => await ViewModel.RefreshAsync(silent: true);
        ViewModel.BlueprintWritten += OnBlueprintWritten;
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
