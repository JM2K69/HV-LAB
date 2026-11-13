using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using HVLab.Helpers;
using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace HVLab.Views;

public sealed partial class VirtualMachinesPage : Page
{
    public VirtualMachinesViewModel ViewModel { get; } = App.GetService<VirtualMachinesViewModel>();
    private LocalizationService Loc => LocalizationService.Instance;

    /// <summary>Gen / CPU / VLAN columns: collapsed when the list is narrower than 900 px.</summary>
    public Visibility DetailColumnsVisibility
    {
        get => (Visibility)GetValue(DetailColumnsVisibilityProperty);
        set => SetValue(DetailColumnsVisibilityProperty, value);
    }

    public static readonly DependencyProperty DetailColumnsVisibilityProperty =
        DependencyProperty.Register(nameof(DetailColumnsVisibility), typeof(Visibility),
                                    typeof(VirtualMachinesPage), new PropertyMetadata(Visibility.Visible));

    /// <summary>Right margin of the column headers: the width taken by the detail pane.</summary>
    public Thickness HeaderMargin
    {
        get => (Thickness)GetValue(HeaderMarginProperty);
        set => SetValue(HeaderMarginProperty, value);
    }

    public static readonly DependencyProperty HeaderMarginProperty =
        DependencyProperty.Register(nameof(HeaderMargin), typeof(Thickness),
                                    typeof(VirtualMachinesPage), new PropertyMetadata(new Thickness(0)));

    private const double PaneWidth = 340 + 16;   // pane + column spacing

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutForPane();

    private void UpdateLayoutForPane()
    {
        var pane = ViewModel.HasSingleSelection ? PaneWidth : 0;
        HeaderMargin = new Thickness(0, 0, pane, 0);
        DetailColumnsVisibility = ActualWidth - pane < 900 ? Visibility.Collapsed : Visibility.Visible;
    }

    // WMI makes a refresh cheap: the list polls every 3 s and the screen of the selected VM every 2 s
    private readonly DispatcherQueueTimer _pollTimer;
    private readonly DispatcherQueueTimer _screenTimer;

    public VirtualMachinesPage()
    {
        InitializeComponent();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(3);
        _pollTimer.Tick += async (_, _) => await ViewModel.RefreshAsync(silent: true);
        _screenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _screenTimer.Interval = TimeSpan.FromSeconds(2);
        _screenTimer.Tick += async (_, _) => await ViewModel.RefreshThumbnailAsync();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
        _pollTimer.Start();
        _screenTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _pollTimer.Stop();
        _screenTimer.Stop();
        base.OnNavigatedFrom(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VirtualMachinesViewModel.HasSingleSelection):
                UpdateLayoutForPane();
                if (ViewModel.HasSingleSelection) _ = ViewModel.RefreshThumbnailAsync();
                break;
            case nameof(VirtualMachinesViewModel.Thumbnail):
                ShowThumbnail(ViewModel.Thumbnail);
                break;
        }
    }

    private void ShowThumbnail(VmThumbnail? thumbnail)
    {
        NoScreenText.Visibility = thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
        if (thumbnail is null) { ScreenImage.Source = null; return; }
        var bitmap = ScreenImage.Source as WriteableBitmap;
        if (bitmap is null || bitmap.PixelWidth != thumbnail.Width || bitmap.PixelHeight != thumbnail.Height)
            bitmap = new WriteableBitmap(thumbnail.Width, thumbnail.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(thumbnail.Bgra, 0, thumbnail.Bgra.Length);
        bitmap.Invalidate();
        ScreenImage.Source = bitmap;
    }

    private void VmList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SetSelection(VmList.SelectedItems.OfType<VirtualMachine>());

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

    // ── "More actions" menu: the row's VM is in the item's Tag (flyouts do not see the page by ElementName)
    private static VirtualMachine? VmOf(object sender) => (sender as FrameworkElement)?.Tag as VirtualMachine;

    private void TurnOff_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ViewModel.TurnOffVmCommand.Execute(vm); }
    private void Restart_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) ViewModel.RestartVmCommand.Execute(vm); }
    private void Reset_Click(object sender, RoutedEventArgs e)   { if (VmOf(sender) is { } vm) ViewModel.ResetVmCommand.Execute(vm); }
    private void Save_Click(object sender, RoutedEventArgs e)    { if (VmOf(sender) is { } vm) ViewModel.SaveVmCommand.Execute(vm); }
    private void Pause_Click(object sender, RoutedEventArgs e)   { if (VmOf(sender) is { } vm) ViewModel.PauseVmCommand.Execute(vm); }
    private void Resume_Click(object sender, RoutedEventArgs e)  { if (VmOf(sender) is { } vm) ViewModel.ResumeVmCommand.Execute(vm); }
    private void Delete_Click(object sender, RoutedEventArgs e)  { if (VmOf(sender) is { } vm) ViewModel.RemoveVmCommand.Execute(vm); }
    private async void Settings_Click(object sender, RoutedEventArgs e) { if (VmOf(sender) is { } vm) await ShowSettingsAsync(vm); }

    // ── Detail pane: connections and clipboard (view concerns: external tools, clipboard)

    private void Console_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVm is { } vm) ViewModel.ConnectVmCommand.Execute(vm);
    }

    private static bool IsSafeArgument(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9._-]+$");

    /// <summary>SSH in Windows Terminal (or a console) with the account HV-LAB wrote into the notes.</summary>
    private void Ssh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVm is not { } vm || InputValidator.ParseIpv4(vm.IpAddress) is null) return;
        var account = vm.Credentials.Account;
        var target = IsSafeArgument(account) ? $"{account}@{vm.IpAddress}" : vm.IpAddress;
        try { Process.Start(new ProcessStartInfo("wt.exe", $"ssh {target}") { UseShellExecute = true }); }
        catch (Exception) { Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh {target}") { UseShellExecute = true }); }
    }

    private void Rdp_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVm is { } vm && InputValidator.ParseIpv4(vm.IpAddress) is not null)
            Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{vm.IpAddress}") { UseShellExecute = true });
    }

    private void CopyIp_Click(object sender, RoutedEventArgs e) => Copy(ViewModel.SelectedVm?.IpAddress);
    private void CopyPassword_Click(object sender, RoutedEventArgs e) => Copy(ViewModel.SelectedVm?.Credentials.Password);

    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.Status = Loc["VM_Copied"];
    }

    // ── Settings dialog

    private async void PaneSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVm is { } vm) await ShowSettingsAsync(vm);
    }

    private async Task ShowSettingsAsync(VirtualMachine vm)
    {
        if (!await ViewModel.LoadSettingsAsync(vm)) return;
        SettingsError.IsOpen = false;
        SettingsDialog.XamlRoot = XamlRoot;
        if (await SettingsDialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ApplySettingsAsync(vm);
    }

    private void SettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.ValidateSettings() is { } error)
        {
            SettingsError.Message = error;
            SettingsError.IsOpen = true;
            args.Cancel = true;
        }
    }

    private void BrowseIso_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainAppWindow);
        var file = Win32FolderPicker.PickFile(hwnd, Loc["SETVM_Iso"], "ISO", "*.iso");
        if (file is not null && ViewModel.EditingSettings is { } s) s.IsoPath = file;
    }

    private void EjectIso_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.EditingSettings is { } s) s.IsoPath = "";
    }

    // Navigation is a view concern: stays in code-behind
    private void CreateVm_Click(object sender, RoutedEventArgs e)
        => App.MainAppWindow?.NavigateTo("quickvm");
}
