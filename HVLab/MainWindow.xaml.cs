using HVLab.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HVLab;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // ── Mica backdrop ────────────────────────────────────────────────────
        SystemBackdrop = new MicaBackdrop();

        // ── Custom title bar ─────────────────────────────────────────────────
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // ── Window size ──────────────────────────────────────────────────────
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));
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
            "settings"  => typeof(SettingsPage),
            _           => (Type?)null
        };
        if (pageType is not null)
            ContentFrame.Navigate(pageType);
    }
}

