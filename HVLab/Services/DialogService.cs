using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HVLab.Services;

/// <summary>
/// ContentDialog-based <see cref="IDialogService"/>, shown in the main window by default, or in the window whose
/// XamlRoot <paramref name="root"/> returns (a VM window shows its own dialogs).
/// </summary>
public sealed class DialogService(Func<XamlRoot?>? root = null) : IDialogService
{
    private XamlRoot? Root => root is not null ? root() : App.MainAppWindow?.Content?.XamlRoot;

    public async Task<bool> ConfirmAsync(string title, string content, string primaryButtonText)
    {
        var xamlRoot = Root;
        if (xamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            Title             = title,
            Content           = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText   = LocalizationService.Instance["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,   // destructive actions: Cancel by default
            XamlRoot          = xamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string content, string primaryButtonText,
                                                                           string? optionLabel, bool optionDefault)
    {
        var xamlRoot = Root;
        if (xamlRoot is null) return (false, false);

        var option = new CheckBox { Content = optionLabel, IsChecked = optionDefault, Margin = new(0, 12, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = content, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        if (optionLabel is not null) body.Children.Add(option);
        var dialog = new ContentDialog
        {
            Title             = title,
            Content           = new ScrollViewer { Content = body, MaxHeight = 420 },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText   = LocalizationService.Instance["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = xamlRoot,
        };
        var confirmed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (confirmed, confirmed && optionLabel is not null && option.IsChecked == true);
    }

    public async Task<string?> PromptAsync(string title, string label, string initialValue)
    {
        var xamlRoot = Root;
        if (xamlRoot is null) return null;

        var input = new TextBox { Header = label, Text = initialValue, MinWidth = 320 };
        input.Loaded += (_, _) => { input.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); input.SelectAll(); };
        var dialog = new ContentDialog
        {
            Title             = title,
            Content           = input,
            PrimaryButtonText = LocalizationService.Instance["Btn_Ok"],
            CloseButtonText   = LocalizationService.Instance["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = xamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && input.Text.Trim() is { Length: > 0 } text ? text : null;
    }

    public async Task<string?> PromptTextAsync(string title, string label, string initialValue)
    {
        var xamlRoot = Root;
        if (xamlRoot is null) return null;

        var input = new TextBox
        {
            Header = label, Text = initialValue, AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), MinWidth = 560, Height = 220,
        };
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(input, ScrollBarVisibility.Auto);
        input.Loaded += (_, _) => input.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        var dialog = new ContentDialog
        {
            Title             = title,
            Content           = input,
            PrimaryButtonText = LocalizationService.Instance["GUEST_Run"],
            CloseButtonText   = LocalizationService.Instance["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,   // Enter inserts a line in the script
            XamlRoot          = xamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary && input.Text.Trim().Length > 0 ? input.Text : null;
    }

    public async Task ShowTextAsync(string title, string text)
    {
        var xamlRoot = Root;
        if (xamlRoot is null) return;

        var output = new TextBox
        {
            Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, MinWidth = 560, MaxHeight = 420,
        };
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(output, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(output, ScrollBarVisibility.Auto);
        var dialog = new ContentDialog
        {
            Title           = title,
            Content         = output,
            CloseButtonText = LocalizationService.Instance["Btn_Ok"],
            DefaultButton   = ContentDialogButton.Close,
            XamlRoot        = xamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        await dialog.ShowAsync();
    }
}
