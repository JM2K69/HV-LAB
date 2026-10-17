using CommunityToolkit.Mvvm.ComponentModel;

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

    partial void OnErrorMessageChanged(string value) => OnEmptyStateChanged();
    partial void OnHasLoadedChanged(bool value)      => OnEmptyStateChanged();

    /// <summary>Raise PropertyChanged for the page's IsXxxEmpty properties.</summary>
    protected virtual void OnEmptyStateChanged() { }

    /// <summary>Marks a successful load: clears the error and enables empty states.</summary>
    protected void MarkLoaded()
    {
        ErrorMessage = string.Empty;
        HasLoaded    = true;
        OnEmptyStateChanged();
    }

    protected void ShowError(Exception ex) => ErrorMessage = ex.Message;

    /// <summary>True when a successful load returned no item (and no error is shown).</summary>
    protected bool IsEmptyAfterLoad(int count) => HasLoaded && !HasError && count == 0;
}
