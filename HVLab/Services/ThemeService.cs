using Microsoft.UI.Xaml;

namespace HVLab.Services;

/// <summary>
/// Applies the user's theme preference (System / Light / Dark) to the root
/// FrameworkElement of the main window.  By default "System" follows the
/// Windows accent / personalization settings via Mica.
/// </summary>
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
}
