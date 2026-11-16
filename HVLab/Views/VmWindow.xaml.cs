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
using Microsoft.UI.Xaml.Input;

namespace HVLab.Views;

/// <summary>Tabs of the VM window, in the order of the Pivot items.</summary>
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
        window.KeepInFront();
    }

    /// <summary>
    /// Opened from a double-click, the window is activated while the main window still handles that click: the list
    /// then takes the focus back and the VM window drops behind it. Once the click is over, the VM window comes back to
    /// the front, unless the user has meanwhile gone to another application (never steals the focus from it).
    /// </summary>
    private void KeepInFront()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = false;
        timer.Tick += (t, _) =>
        {
            t.Stop();
            var hwnd  = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var front = GetForegroundWindow();
            GetWindowThreadProcessId(front, out var pid);
            if (front == hwnd || pid != (uint)Environment.ProcessId) return;
            AppWindow.MoveInZOrderAtTop();
            SetForegroundWindow(hwnd);
            Activate();
        };
        timer.Start();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    /// <summary>Opens the window of a VM just created (its name is unique: namesakes are refused at creation).</summary>
    public static async Task ShowByNameAsync(string name)
    {
        var vm = (await App.GetService<IHyperVService>().GetVirtualMachinesAsync()).FirstOrDefault(v => v.Name == name);
        if (vm is not null) Show(vm);
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
        // Nothing is polled while the window is minimized (no WMI query for a window nobody sees)
        _refreshTimer.Tick += async (_, _) => { if (!IsMinimized) await ViewModel.RefreshAsync(); };
        _screenTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _screenTimer.Interval = TimeSpan.FromSeconds(2);
        _screenTimer.Tick += async (_, _) => { if (!IsMinimized) await RefreshScreenAsync(); };
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

        // F5 refreshes the VM (and the checkpoint tree)
        var refresh = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 };
        refresh.Invoked += async (_, args) =>
        {
            args.Handled = true;
            await ViewModel.RefreshAsync();
            if (Tabs.SelectedIndex == (int)VmWindowTab.Checkpoints) await ViewModel.LoadCheckpointsAsync();
        };
        RootGrid.KeyboardAccelerators.Add(refresh);

        // Not smaller than the layout needs (header buttons, two-column settings)
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            presenter.PreferredMinimumWidth  = (int)(820 * scale);
            presenter.PreferredMinimumHeight = (int)(560 * scale);
        }

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
        var tab = (VmWindowTab)Tabs.SelectedIndex;
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
        }
    }

    /// <summary>Copies a frame into the image's bitmap (reused while the size stays).</summary>
    private static void Render(Image image, TextBlock placeholder, VmThumbnail? frame)
    {
        placeholder.Visibility = frame is null ? Visibility.Visible : Visibility.Collapsed;
        if (frame is null) { image.Source = null; return; }
        var bitmap = image.Source as WriteableBitmap;
        var resized = bitmap is null || bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height;
        if (resized) bitmap = new WriteableBitmap(frame.Width, frame.Height);
        using (var stream = bitmap!.PixelBuffer.AsStream())
            stream.Write(frame.Bgra, 0, frame.Bgra.Length);
        bitmap.Invalidate();
        image.Source = bitmap;
    }

    private bool IsMinimized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    // ── Connections and clipboard (view concerns: external tools, clipboard)

    /// <summary>Account name passed to ssh: letters, digits, dot, dash, underscore, never a leading dash (an option).</summary>
    private static bool IsSafeArgument(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9._][A-Za-z0-9._-]*$");

    /// <summary>SSH in Windows Terminal (or a console) with the account HV-LAB wrote into the notes.</summary>
    private void Ssh_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel.Vm;
        if (InputValidator.ParseIpv4(vm.IpAddress) is null) return;
        var target = IsSafeArgument(vm.Credentials.Account) ? $"{vm.Credentials.Account}@{vm.IpAddress}" : vm.IpAddress;
        // As the user, unelevated: Windows Terminal, else a console; elevated fallback only from absolute paths
        if (!UnelevatedLauncher.TryStart("wt.exe", $"ssh {target}"))
            UnelevatedLauncher.Start(UnelevatedLauncher.System32("cmd.exe"), $"/k ssh {target}",
                                     UnelevatedLauncher.System32("cmd.exe"), $"/k \"{UnelevatedLauncher.System32(@"OpenSSH\ssh.exe")}\" {target}");
    }

    private void Rdp_Click(object sender, RoutedEventArgs e)
    {
        var ip = ViewModel.Vm.IpAddress;
        if (InputValidator.ParseIpv4(ip) is null) return;
        var mstsc = UnelevatedLauncher.System32("mstsc.exe");
        UnelevatedLauncher.Start(mstsc, $"/v:{ip}", mstsc, $"/v:{ip}");
    }

    private void CopyIp_Click(object sender, RoutedEventArgs e) => Copy(ViewModel.Vm.IpAddress);
    private void CopyErrorDetails_Click(object sender, RoutedEventArgs e) => Copy($"{ViewModel.ErrorMessage}\n\n{ViewModel.ErrorDetails}");
    /// <summary>
    /// The password is kept out of the clipboard history and the cloud clipboard, and cleared after 30 s
    /// unless something else has been copied meanwhile.
    /// </summary>
    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        var password = ViewModel.Vm.Credentials.Password;
        if (string.IsNullOrEmpty(password)) return;
        var package = new DataPackage();
        package.SetText(password);
        Clipboard.SetContentWithOptions(package, new ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false });
        ViewModel.Status = Loc["VM_CopiedSecret"];

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.IsRepeating = false;
        timer.Tick += async (t, _) =>
        {
            t.Stop();
            try
            {
                var content = Clipboard.GetContent();
                if (content.Contains(StandardDataFormats.Text) && await content.GetTextAsync() == password) Clipboard.Clear();
            }
            catch (Exception) { /* clipboard busy: leave it */ }
        };
        timer.Start();
    }

    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.Status = Loc["VM_Copied"];
    }

    private async void CopyToGuest_Click(object sender, RoutedEventArgs e)
    {
        var file = Win32FolderPicker.PickFile(WinRT.Interop.WindowNative.GetWindowHandle(this), Loc["GUEST_CopyButton"], "*", "*.*");
        if (file is not null) await ViewModel.CopyToGuestAsync(file);
    }

    private void DeleteCheckpoint_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.RemoveCheckpointCommand.CanExecute(null)) ViewModel.RemoveCheckpointCommand.Execute(null);
        args.Handled = true;
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
