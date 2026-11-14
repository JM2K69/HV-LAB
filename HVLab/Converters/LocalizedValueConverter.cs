using HVLab.Services;
using Microsoft.UI.Xaml.Data;

namespace HVLab.Converters;

/// <summary>
/// Shows an internal English value in the UI language: ConverterParameter is the key prefix
/// ("SwType_" + "Internal" → "Interne"). For list items (ComboBox, RadioButtons) whose value stays the key.
/// </summary>
public sealed class LocalizedValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => LocalizationService.Instance.Display(parameter as string ?? "", value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
