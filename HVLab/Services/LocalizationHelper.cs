using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HVLab.Services;

/// <summary>
/// XAML-instanciable wrapper around <see cref="LocalizationService"/> that
/// forwards string lookups via the indexer and forwards all PropertyChanged
/// notifications so that classic {Binding [key]} expressions refresh when the
/// language changes.
/// </summary>
public sealed class LocalizationHelper : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizationHelper()
    {
        LocalizationService.Instance.PropertyChanged += (_, e) =>
            PropertyChanged?.Invoke(this, e);
    }

    public string this[string key] => LocalizationService.Instance[key];
}
