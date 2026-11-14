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
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace HVLab.Views;

/// <summary>Tabs of the VM window, in the order of the Pivot items.</summary>
public enum VmWindowTab { Overview, Console, Checkpoints, Settings }

/// <summary>
/// One VM in its own window (one window per VM): screen, integrated console, details, credentials, checkpoints and settings,
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
    private readonly DispatcherQueueTimer _consoleTimer;

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
        // Console frames while the Console tab is shown: one request at a time (~200 ms each at 1024×768), back to back
        _consoleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _consoleTimer.Interval = TimeSpan.FromMilliseconds(40);
        _consoleTimer.Tick += async (_, _) => await NextConsoleFrameAsync();
        _refreshTimer.Start();
        _screenTimer.Start();

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.VmRemoved += (_, _) => Close();
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _screenTimer.Stop();
            _consoleTimer.Stop();
            OpenWindows.Remove(_vmId);
        };

        // A click anywhere outside the console screen gives keyboard and mouse back to the host
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Root_PointerPressed), true);

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

    // ── Tabs: the screen refreshes on the overview only, the console on its tab; checkpoints and settings load when shown

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var tab = (VmWindowTab)Tabs.SelectedIndex;
        if (tab == VmWindowTab.Console) { _consoleTimer.Start(); _ = NextConsoleFrameAsync(); }
        else { _consoleTimer.Stop(); SetFullScreen(false); }

        switch (tab)
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
        switch (e.PropertyName)
        {
            case nameof(VmDetailsViewModel.Thumbnail):
                Render(ScreenImage, NoScreenText, ViewModel.Thumbnail);
                break;
            case nameof(VmDetailsViewModel.ConsoleFrame):
                if (Render(ConsoleImage, NoConsoleText, ViewModel.ConsoleFrame)) UpdateConsoleLayout();
                break;
            case nameof(VmDetailsViewModel.ZoomIndex):
                UpdateConsoleLayout();
                break;
        }
    }

    /// <summary>Copies a frame into the image's bitmap (reused while the size stays); true when the size changed.</summary>
    private static bool Render(Image image, TextBlock placeholder, VmThumbnail? frame)
    {
        placeholder.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        if (frame is null) { image.Source = null; return false; }
        var bitmap = image.Source as WriteableBitmap;
        var resized = bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height;
        if (resized) bitmap = new WriteableBitmap(frame.Width, frame.Height);
        using (var stream = bitmap!.PixelBuffer.AsStream())
            stream.Write(frame.Bgra, 0, frame.Bgra.Length);
        bitmap.Invalidate();
        image.Source = bitmap;
        return resized;
    }

    // ── Console tab: frames, zoom, keyboard and mouse (view concerns; the input mapping lives in ConsoleInput / the view model)

    private bool _consoleBusy;
    private bool _captured;
    private bool _fullScreen;
    private readonly HashSet<(uint Code, bool Extended)> _keysDown = [];

    private async Task NextConsoleFrameAsync()
    {
        if (_consoleBusy) return;
        _consoleBusy = true;
        try { await ViewModel.RefreshConsoleAsync(); }
        finally { _consoleBusy = false; }
    }

    private void ConsoleScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateConsoleLayout();

    /// <summary>
    /// Fit: the whole screen in the tab, no scrollbars. Zoom N %: N % of the guest pixels on physical pixels
    /// (100 % = pixel for pixel whatever the Windows scaling), with scrollbars when larger than the tab.
    /// </summary>
    private void UpdateConsoleLayout()
    {
        if (ViewModel.ConsoleFrame is not { } frame) return;
        double width, height;
        if (ViewModel.IsFit)
        {
            ConsoleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            ConsoleScroll.VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled;
            var (vw, vh) = (ConsoleScroll.ActualWidth, ConsoleScroll.ActualHeight);
            if (vw <= 0 || vh <= 0) return;
            var k = Math.Min(vw / frame.Width, vh / frame.Height);
            (width, height) = (frame.Width * k, frame.Height * k);
        }
        else
        {
            ConsoleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            ConsoleScroll.VerticalScrollBarVisibility   = ScrollBarVisibility.Auto;
            var k = ViewModel.Zoom / 100.0 / (Content?.XamlRoot?.RasterizationScale ?? 1.0);
            (width, height) = (frame.Width * k, frame.Height * k);
        }
        ConsoleImage.Width  = Math.Max(1, Math.Floor(width));
        ConsoleImage.Height = Math.Max(1, Math.Floor(height));
    }

    // Keyboard: physical scancodes, so the guest applies its own layout. Ctrl+Alt+Del stays with the host (button).

    private async void Console_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.Vm.IsRunning) return;
        e.Handled = true;   // Tab, arrows, Esc… go to the VM, not to the window
        var key = (e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);
        _keysDown.Add(key);
        await ViewModel.SendKeyAsync(key.ScanCode, key.IsExtendedKey, keyUp: false);
    }

    private async void Console_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!ViewModel.Vm.IsRunning) return;
        e.Handled = true;
        var key = (e.KeyStatus.ScanCode, e.KeyStatus.IsExtendedKey);
        _keysDown.Remove(key);
        await ViewModel.SendKeyAsync(key.ScanCode, key.IsExtendedKey, keyUp: true);
    }

    private void Console_GotFocus(object sender, RoutedEventArgs e) => SetCaptured(true);

    private async void Console_LostFocus(object sender, RoutedEventArgs e)
    {
        SetCaptured(false);
        // Keys still held when the focus leaves (Alt+Tab…) are released in the guest, so none stays stuck
        var held = _keysDown.ToList();
        _keysDown.Clear();
        foreach (var (code, extended) in held)
            await ViewModel.SendKeyAsync(code, extended, keyUp: true);
    }

    private void SetCaptured(bool captured)
    {
        _captured = captured;
        ConsoleBorder.BorderBrush = captured && Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent)
            ? (Brush)accent
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ConsoleHint.Text = Loc[captured ? "CON_Captured" : "CON_Hint"];
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_captured && !IsInside(e.OriginalSource as DependencyObject, ConsoleImage))
            ZoomBox.Focus(FocusState.Programmatic);
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        for (var d = element; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d == container) return true;
        return false;
    }

    // Mouse: absolute guest pixels from the position on the zoomed image; buttons follow the pointer updates

    private bool TryGuestPoint(PointerPoint point, bool clamp, out (int X, int Y) guest)
    {
        guest = default;
        if (ViewModel.ConsoleFrame is not { } frame || !ViewModel.Vm.IsRunning) return false;
        var (w, h) = (ConsoleImage.ActualWidth, ConsoleImage.ActualHeight);
        var p = point.Position;
        if (!clamp && (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h)) return false;
        guest = ConsoleInput.ToGuest(p.X, p.Y, w, h, frame.Width, frame.Height);
        return true;
    }

    private static (int Button, bool Down) ButtonOf(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed    => (1, true),
        PointerUpdateKind.LeftButtonReleased   => (1, false),
        PointerUpdateKind.RightButtonPressed   => (2, true),
        PointerUpdateKind.RightButtonReleased  => (2, false),
        PointerUpdateKind.MiddleButtonPressed  => (3, true),
        PointerUpdateKind.MiddleButtonReleased => (3, false),
        _ => (0, false),
    };

    private void SendPointer(PointerPoint point, bool clamp)
    {
        if (!TryGuestPoint(point, clamp, out var guest)) return;
        ViewModel.MoveMouse(guest.X, guest.Y);
        var (button, down) = ButtonOf(point.Properties.PointerUpdateKind);
        if (button > 0) _ = ViewModel.MouseButtonAsync(button, down);
    }

    private void ConsoleImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.Vm.IsRunning) return;
        ConsoleSurface.Focus(FocusState.Pointer);
        ConsoleImage.CapturePointer(e.Pointer);   // the release arrives even outside the image
        SendPointer(e.GetCurrentPoint(ConsoleImage), clamp: false);
        e.Handled = true;
    }

    private void ConsoleImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_captured) return;
        var point = e.GetCurrentPoint(ConsoleImage);
        SendPointer(point, clamp: true);
        var p = point.Properties;
        if (!p.IsLeftButtonPressed && !p.IsRightButtonPressed && !p.IsMiddleButtonPressed)
            ConsoleImage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ConsoleImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_captured) return;
        var point = e.GetCurrentPoint(ConsoleImage);
        var p = point.Properties;
        SendPointer(point, clamp: p.IsLeftButtonPressed || p.IsRightButtonPressed || p.IsMiddleButtonPressed);
    }

    /// <summary>Ctrl + wheel zooms; otherwise the wheel goes to the VM while it has the input, else it scrolls the view.</summary>
    private void ConsoleImage_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ConsoleImage).Properties.MouseWheelDelta;
        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            ViewModel.StepZoom(delta > 0);
            e.Handled = true;
        }
        else if (_captured && ViewModel.Vm.IsRunning && delta != 0)
        {
            _ = ViewModel.MouseWheelAsync(delta);
            e.Handled = true;
        }
    }

    private async void TypeClipboard_Click(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        var text = content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null;
        await ViewModel.TypeTextAsync(text);
    }

    // Full screen: the console alone on the screen (title bar, header and status hidden)

    private void FullScreen_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;
        AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        RootGrid.RowDefinitions[0].Height = new GridLength(on ? 0 : 48);
        AppTitleBar.Visibility = HeaderGrid.Visibility = StatusText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        BodyGrid.Padding = on ? new Thickness(8, 0, 8, 8) : new Thickness(24, 4, 24, 16);
        FullScreenIcon.Glyph = on ? "" : "";
        FullScreenText.Text  = Loc[on ? "CON_ExitFullScreen" : "CON_FullScreen"];
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
