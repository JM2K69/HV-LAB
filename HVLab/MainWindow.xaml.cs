using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HVLab.ViewModels;
using HVLab.Models;

namespace HVLab;

public sealed partial class MainWindow : Window
{
    private MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        
        _viewModel = new MainViewModel();
        InitializeBindings();
    }

    private void InitializeBindings()
    {
        SwitchesGrid.ItemsSource = _viewModel.VirtualSwitches;
        VMsGrid.ItemsSource = _viewModel.VirtualMachines;
        VhdxGrid.ItemsSource = _viewModel.BaseVhdxImages;
    }

    private async void RefreshSwitches_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshVirtualSwitchesCommand.ExecuteAsync(null);
        UpdateStatus(_viewModel.StatusMessage);
        SwitchCountText.Text = _viewModel.VirtualSwitches.Count.ToString();
    }

    private void CreateSwitch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Create Virtual Switch",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            Content = new StackPanel
            {
                Spacing = 15,
                Padding = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = "Switch Name:" },
                    new TextBox { PlaceholderText = "e.g., MySwitch" },
                    new TextBlock { Text = "IP Address:" },
                    new TextBox { PlaceholderText = "e.g., 192.168.100.1" },
                    new TextBlock { Text = "Subnet Mask:" },
                    new TextBox { PlaceholderText = "e.g., 255.255.255.0" }
                }
            },
            XamlRoot = Content.XamlRoot
        };
        
        UpdateStatus("Dialog for creating virtual switch would open here");
    }

    private async void RefreshVMs_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshVirtualMachinesCommand.ExecuteAsync(null);
        UpdateStatus(_viewModel.StatusMessage);
        VMCountText.Text = _viewModel.VirtualMachines.Count.ToString();
    }

    private void CreateVM_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Dialog for creating VM would open here");
    }

    private void CreateVhdx_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatus("Dialog for creating VHDX would open here");
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
        LoadingSpinner.IsActive = _viewModel.IsLoading;
    }
}

