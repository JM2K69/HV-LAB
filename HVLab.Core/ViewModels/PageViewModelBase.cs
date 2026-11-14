using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Services;

namespace HVLab.ViewModels;

/// <summary>
/// Error and loaded state shared by the list pages: the last error is shown in an InfoBar,
/// and the empty state only appears once a refresh has succeeded.
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>Last error; empty when the last operation succeeded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty] private bool hasLoaded;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>What PowerShell reported behind the error (the "Copy details" button of the banner); empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorDetails))]
    private string errorDetails = string.Empty;

    public bool HasErrorDetails => !string.IsNullOrEmpty(ErrorDetails);

    private static string DetailsOf(Exception ex)
        => ex is PowerShellException { Details.Length: > 0 } ps ? ps.Details : string.Empty;

    // A new message drops the details of the previous error
    partial void OnErrorMessageChanging(string value) => ErrorDetails = string.Empty;

    partial void OnErrorMessageChanged(string value) => OnEmptyStateChanged();
    partial void OnHasLoadedChanged(bool value)      => OnEmptyStateChanged();

    /// <summary>Raise PropertyChanged for the page's IsXxxEmpty properties.</summary>
    protected virtual void OnEmptyStateChanged() { }

    // True while the error shown comes from a user action: a background refresh must not hide it
    private bool _actionError;

    /// <summary>Marks a successful load: clears a load error (not an action error) and enables empty states.</summary>
    protected void MarkLoaded()
    {
        if (!_actionError) ErrorMessage = string.Empty;
        HasLoaded = true;
        OnEmptyStateChanged();
    }

    /// <summary>Load error: cleared by the next successful load.</summary>
    protected void ShowError(Exception ex)
    {
        ErrorMessage = ex.Message;
        ErrorDetails = DetailsOf(ex);
        _actionError = false;
    }

    /// <summary>Action error from an exception: its message, and PowerShell's own output as the details.</summary>
    protected void ShowActionError(Exception ex, string? prefix = null)
    {
        ShowActionError(prefix is null ? ex.Message : $"{prefix} {ex.Message}");
        ErrorDetails = DetailsOf(ex);
    }

    /// <summary>Error of a user action (start, delete…): stays until the banner is closed or the next action starts.</summary>
    protected void ShowActionError(string message)
    {
        ErrorMessage = message;
        _actionError = true;
    }

    /// <summary>A new action starts: the previous action's error no longer applies.</summary>
    protected void ClearActionError()
    {
        if (_actionError) DismissError();
    }

    /// <summary>Close button of the error banner.</summary>
    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = string.Empty;
        _actionError = false;
    }

    /// <summary>True when a successful load returned no item (and no error is shown).</summary>
    protected bool IsEmptyAfterLoad(int count) => HasLoaded && !HasError && count == 0;
}
