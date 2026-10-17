using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace HVLab.Services;

/// <summary>
/// Applies the user's theme preference (System / Light / Dark) to the root
/// FrameworkElement of the main window.  By default "System" follows the
/// Windows accent / personalization settings via Mica.
/// </summary>
/// <summary><see cref="IThemeService"/> for the main window.</summary>
public sealed class WindowThemeService : IThemeService
{
    public void Apply(string theme)
    {
        if (App.MainAppWindow?.Content is FrameworkElement root)
            ThemeService.Apply(root, theme);
    }
}

public static class ThemeService
{
    /// <summary>
    /// Converts a stored theme string to a <see cref="ElementTheme"/> value.
    /// </summary>
    public static ElementTheme ToElementTheme(string? theme) => theme switch
    {
        "Light"  => ElementTheme.Light,
        "Dark"   => ElementTheme.Dark,
        _        => ElementTheme.Default   // "System" or anything unrecognised
    };

    /// <summary>
    /// Applies <paramref name="theme"/> to <paramref name="root"/> immediately.
    /// Call this whenever the user changes the theme in Settings.
    /// </summary>
    public static void Apply(FrameworkElement root, string theme)
    {
        root.RequestedTheme = ToElementTheme(theme);
    }

    /// <summary>
    /// Applies the persisted theme from <see cref="AppSettings.Current"/> to
    /// the given root element.
    /// </summary>
    public static void ApplyCurrent(FrameworkElement root)
        => Apply(root, AppSettings.Current.Theme);

    /// <summary>
    /// Makes the caption buttons (min / max / close) of a window whose content extends into the
    /// title bar follow the app theme instead of the OS theme. Call it again from
    /// <see cref="FrameworkElement.ActualThemeChanged"/>.
    /// </summary>
    public static void ApplyTitleBar(AppWindow appWindow, ElementTheme actualTheme)
        => appWindow.TitleBar.PreferredTheme =
               actualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
}
