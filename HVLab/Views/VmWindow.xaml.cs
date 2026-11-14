using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using HVLab.Helpers;
using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace HVLab.Views;

public enum VmWindowTab { Overview, Checkpoints, Settings }

/// <summary>
/// One VM in its own window (one window per VM): screen, details, credentials, checkpoints and settings,
/// with tabs instead of a long scrolling pane. Opened from the VM list (double-click or the details button).
/// </summary>
public sealed partial class VmWindow : Window
{
    private static readonly Dictionary<string, VmWindow> OpenWindows = [];

    /// <summary>Opens the VM's window on <paramref name="tab"/>, or brings the open one to the front.</summary>
    public static void Show(VirtualMachine vm, VmWindowTab tab = VmWindowTab.Overview)
    {
        if (!OpenWindows.TryGetValue(vm.Id, out var window))
        {
            window = new VmWindow(vm);
            OpenWindows[vm.Id] = window;
        }
        window.Tabs.SelectedIndex = (int)tab;
        window.Activate();
    }

    /// <summary>Keeps the open VM windows on the theme chosen in Settings.</summary>
    public static void ApplyThemeToAll(string theme)
    {
        foreach (var w in OpenWindows.Values) ThemeService.Apply(w.RootGrid, theme);
    }

    public VmDetailsViewModel ViewModel { get; }
    private LocalizationService Loc => LocalizationService.Instance;
    private readonly string _vmId;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _screenTimer;

    private VmWindow(VirtualMachine vm)
    {
        _vmId = vm.Id;
        // Dialogs of this window show in this window, not in the main one
        ViewModel = new VmDetailsViewModel(App.GetService<IHyperVService>(), new DialogService(() => Content?.XamlRoot));
        ViewModel.Initialize(vm);
        InitializeComponent();

        Title = $"{vm.Name} — HV-LAB";
        TitleText.Text = Title;
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.ico"));
        ThemeService.ApplyCurrent(RootGrid);
        RootGrid.ActualThemeChanged += (s, _) => ThemeService.ApplyTitleBar(AppWindow, s.ActualTheme);
        RootGrid.Loaded             += (s, _) => ThemeService.ApplyTitleBar(AppWindow, RootGrid.ActualTheme);
        PlaceNextToMainWindow();

        // The window keeps its VM live on its own (WMI: cheap), whatever page the main window shows
        _refreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(3);
        _refreshTimer.Tick += async (_, _) => await ViewModel.RefreshAsync();
        _screenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _screenTimer.Interval = TimeSpan.FromSeconds(2);
        _screenTimer.Tick += async (_, _) => await RefreshScreenAsync();
        _refreshTimer.Start();
        _screenTimer.Start();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.VmRemoved += (_, _) => Close();
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _screenTimer.Stop();
            OpenWindows.Remove(_vmId);
        };

        _ = RefreshScreenAsync();
        _ = ViewModel.LoadCheckpointsAsync();
    }

    /// <summary>1100×780 DIP, centered on the monitor of the main window.</summary>
    private void PlaceNextToMainWindow()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var anchor = App.MainAppWindow?.AppWindow.Id ?? AppWindow.Id;
        var work = DisplayArea.GetFromWindowId(anchor, DisplayAreaFallback.Nearest).WorkArea;
        var w = Math.Min((int)(1100 * scale), work.Width);
        var h = Math.Min((int)(780 * scale), work.Height);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // ── Tabs: the screen refreshes on the overview only; checkpoints and settings load when shown

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        switch ((VmWindowTab)Tabs.SelectedIndex)
        {
            case VmWindowTab.Checkpoints: await ViewModel.LoadCheckpointsAsync(); break;
            case VmWindowTab.Settings when ViewModel.EditingSettings is null: await ViewModel.LoadSettingsAsync(); break;
        }
    }

    private Task RefreshScreenAsync()
        => Tabs.SelectedIndex == (int)VmWindowTab.Overview
            ? ViewModel.RefreshThumbnailAsync(1024, 768)
            : Task.CompletedTask;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VmDetailsViewModel.Thumbnail)) ShowThumbnail(ViewModel.Thumbnail);
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

    // ── Connections and clipboard (view concerns: external tools, clipboard)

    private static bool IsSafeArgument(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9._-]+$");

    /// <summary>SSH in Windows Terminal (or a console) with the account HV-LAB wrote into the notes.</summary>
    private void Ssh_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.Vm;
        if (InputValidator.ParseIpv4(vm.IpAddress) is null) return;
        var target = IsSafeArgument(vm.Credentials.Account) ? $"{vm.Credentials.Account}@{vm.IpAddress}" : vm.IpAddress;
        try { Process.Start(new ProcessStartInfo("wt.exe", $"ssh {target}") { UseShellExecute = true }); }
        catch (Exception) { Process.Start(new ProcessStartInfo("cmd.exe", $"/k ssh {target}") { UseShellExecute = true }); }
    }

    private void Rdp_Click(object sender, RoutedEventArgs e)
    {
        if (InputValidator.ParseIpv4(ViewModel.Vm.IpAddress) is not null)
            Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{ViewModel.Vm.IpAddress}") { UseShellExecute = true });
    }

    private void CopyIp_Click(object sender, RoutedEventArgs e) => Copy(ViewModel.Vm.IpAddress);
    private void CopyPassword_Click(object sender, RoutedEventArgs e) => Copy(ViewModel.Vm.Credentials.Password);

    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.Status = Loc["VM_Copied"];
    }

    // ── Settings tab: ISO picker

    private void BrowseIso_Click(object sender, RoutedEventArgs e)
    {
        var file = Win32FolderPicker.PickFile(WinRT.Interop.WindowNative.GetWindowHandle(this), Loc["SETVM_Iso"], "ISO", "*.iso");
        if (file is not null && ViewModel.EditingSettings is { } s) s.IsoPath = file;
    }

    private void EjectIso_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.EditingSettings is { } s) s.IsoPath = "";
    }
}
