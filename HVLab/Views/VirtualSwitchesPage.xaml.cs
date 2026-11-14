using HVLab.Models;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualSwitchesPage : Page
{
    public VirtualSwitchesViewModel ViewModel { get; } = App.GetService<VirtualSwitchesViewModel>();

    public VirtualSwitchesPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }

    // ── Change the type of a switch (dialog: a view concern)

    private async void ChangeType_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not VirtualSwitch sw) return;
        ViewModel.BeginChangeType(sw);
        ChangeTypeError.IsOpen = false;
        ChangeTypeDialog.XamlRoot = XamlRoot;
        if (await ChangeTypeDialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.ApplyChangeTypeAsync();
    }

    private void ChangeTypeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.ValidateChangeType() is { } error)
        {
            ChangeTypeError.Message = error;
            ChangeTypeError.IsOpen = true;
            args.Cancel = true;
        }
    }

    // ── Connection sharing: read the current state the first time the section opens (COM call)

    private void IcsExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        if (!ViewModel.IcsLoaded) ViewModel.LoadIcsCommand.Execute(null);
    }
}
