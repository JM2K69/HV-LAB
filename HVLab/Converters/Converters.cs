using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

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

public class StateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var color = value?.ToString() == "Running"
            ? Color.FromArgb(255, 15, 123, 15)
            : Color.FromArgb(255, 196, 43, 28);
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
