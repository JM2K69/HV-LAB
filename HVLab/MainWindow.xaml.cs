using HVLab.Services;
using HVLab.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HVLab;
public sealed partial class MainWindow : Window
{
    public LocalizationService Loc => LocalizationService.Instance;
    public MainWindow()
    {
        InitializeComponent();

        // ── Mica backdrop ────────────────────────────────────────────────────
        SystemBackdrop = new MicaBackdrop();

        // ── Custom title bar ─────────────────────────────────────────────────
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // ── Icône de l'application (barre des tâches / titre) ──────────────
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.ico"));

        // ── Window size ──────────────────────────────────────────────────────
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        ShowEnvironment(AppStartupCache.Instance.Environment);
    }

    private void ShowEnvironment(HyperVEnvironment env)
    {
        EnvInfoBar.IsOpen = env != HyperVEnvironment.Ok;
        EnvInfoBar.Message = env switch
        {
            HyperVEnvironment.FeatureMissing => Loc["Env_FeatureMissing"],
            HyperVEnvironment.ServiceStopped => Loc["Env_ServiceStopped"],
            _ => string.Empty,
        };
        EnvStartServiceButton.Visibility = env == HyperVEnvironment.ServiceStopped
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void EnvStartService_Click(object sender, RoutedEventArgs e)
    {
        var hv = new HyperVService();
        EnvStartServiceButton.IsEnabled = false;
        try
        {
            await hv.StartHyperVServiceAsync();
            ShowEnvironment(await hv.GetEnvironmentStatusAsync());
        }
        catch (Exception ex) { EnvInfoBar.Message = ex.Message; }
        finally { EnvStartServiceButton.IsEnabled = true; }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
        => NavView.SelectedItem = NavView.MenuItems[0];

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var pageType = item.Tag?.ToString() switch
        {
            "dashboard" => typeof(DashboardPage),
            "vms"       => typeof(VirtualMachinesPage),
            "switches"  => typeof(VirtualSwitchesPage),
            "basevhdx"  => typeof(BaseVhdxPage),
            "createvm"  => typeof(CreateVmPage),
            "quickvm"   => typeof(QuickVmPage),
            "settings"  => typeof(SettingsPage),
            _           => (Type?)null
        };
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType, null, args.RecommendedNavigationTransitionInfo);
    }
}

