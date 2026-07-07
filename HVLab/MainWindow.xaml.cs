using HVLab.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HVLab;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
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
}

