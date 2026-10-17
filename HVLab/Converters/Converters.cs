using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace HVLab.Converters;

public class BoolInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not true;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

public class BoolToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not Visibility.Visible;
}

/// <summary>
/// Maps a Hyper-V VM state to a badge colour: success (Running), caution (transitional /
/// Saved / Paused), neutral (Off) and critical (anything unexpected). Fluent status colours
/// dark enough to keep white text readable on both light and dark themes.
/// </summary>
public class StateToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Success  = new(Color.FromArgb(255, 15, 123, 15));
    private static readonly SolidColorBrush Caution  = new(Color.FromArgb(255, 157, 93, 0));
    private static readonly SolidColorBrush Neutral  = new(Color.FromArgb(255, 93, 93, 93));
    private static readonly SolidColorBrush Critical = new(Color.FromArgb(255, 196, 43, 28));

    public object Convert(object value, Type targetType, object parameter, string language)
        => value?.ToString() switch
        {
            "Running" => Success,
            "Off"     => Neutral,
            "Saved" or "Paused" or "Starting" or "Stopping" or "Saving"
                or "Pausing" or "Resuming" or "Reset" => Caution,
            _ => Critical,
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a "#RRGGBB" or "#AARRGGBB" hex string into a <see cref="SolidColorBrush"/>.
/// Falls back to transparent on any parsing error.
/// </summary>
public class StringToSolidColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return new SolidColorBrush(Colors.Transparent);
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = System.Convert.ToByte(hex[0..2], 16);
                byte g = System.Convert.ToByte(hex[2..4], 16);
                byte b = System.Convert.ToByte(hex[4..6], 16);
                return new SolidColorBrush(Color.FromArgb(0xFF, r, g, b));
            }
            if (hex.Length == 8)
            {
                byte a = System.Convert.ToByte(hex[0..2], 16);
                byte r = System.Convert.ToByte(hex[2..4], 16);
                byte g = System.Convert.ToByte(hex[4..6], 16);
                byte b = System.Convert.ToByte(hex[6..8], 16);
                return new SolidColorBrush(Color.FromArgb(a, r, g, b));
            }
        }
        catch { /* fall through */ }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
