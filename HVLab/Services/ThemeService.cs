using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

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
    /// Colours the caption buttons (min / max / close) of a window whose content extends into the
    /// title bar so they match the app theme instead of the OS theme. Call it again from
    /// <see cref="FrameworkElement.ActualThemeChanged"/>.
    /// </summary>
    public static void ApplyTitleBar(AppWindow appWindow, ElementTheme actualTheme)
    {
        var tb   = appWindow.TitleBar;
        var dark = actualTheme == ElementTheme.Dark;
        Color fg = dark ? Colors.White : Colors.Black;

        tb.ButtonBackgroundColor         = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor         = fg;
        tb.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x80, 0, 0, 0);
        tb.ButtonHoverForegroundColor    = fg;
        tb.ButtonHoverBackgroundColor    = dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0, 0, 0);
        tb.ButtonPressedForegroundColor  = fg;
        tb.ButtonPressedBackgroundColor  = dark ? Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0, 0, 0);
    }
}
