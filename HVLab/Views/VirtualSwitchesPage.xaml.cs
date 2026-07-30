using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

public sealed partial class VirtualSwitchesPage : Page
{
	public VirtualSwitchesViewModel ViewModel { get; } = new();
	public LocalizationService Loc => LocalizationService.Instance;

	public VirtualSwitchesPage() => InitializeComponent();

	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		base.OnNavigatedTo(e);
		_ = ViewModel.RefreshAsync();
	}

	private async void RemoveSwitch_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: VirtualSwitch sw }) return;
		var dialog = new ContentDialog
		{
			Title             = Loc["SW_DeleteTitle"],
			Content           = $"{Loc["SW_DeleteConfirm"]} \u00ab {sw.Name} \u00bb ?",
			PrimaryButtonText = Loc["Btn_Delete"],
			CloseButtonText   = Loc["Btn_Cancel"],
			DefaultButton     = ContentDialogButton.Close,
			XamlRoot          = XamlRoot
		};
		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
			await ViewModel.RemoveSwitchAsync(sw);
	}

	private async void RemoveNat_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: NatNetwork nat }) return;
		var dialog = new ContentDialog
		{
			Title             = Loc["NAT_DeleteTitle"],
			Content           = $"{Loc["NAT_DeleteConfirm"]} \u00ab {nat.Name} \u00bb ?",
			PrimaryButtonText = Loc["Btn_Delete"],
			CloseButtonText   = Loc["Btn_Cancel"],
			DefaultButton     = ContentDialogButton.Close,
			XamlRoot          = XamlRoot
		};
		if (await dialog.ShowAsync() == ContentDialogResult.Primary)
			await ViewModel.RemoveNatAsync(nat);
	}
}
