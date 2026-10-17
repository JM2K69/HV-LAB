using Microsoft.UI.Xaml.Controls;

namespace HVLab.Services;

/// <summary>ContentDialog-based <see cref="IDialogService"/> hosted by the main window.</summary>
public sealed class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string content, string primaryButtonText)
    {
        var root = App.MainAppWindow?.Content?.XamlRoot;
        if (root is null) return false;

        var dialog = new ContentDialog
        {
            Title             = title,
            Content           = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText   = LocalizationService.Instance["Btn_Cancel"],
            DefaultButton     = ContentDialogButton.Close,   // destructive actions: Cancel by default
            XamlRoot          = root,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
