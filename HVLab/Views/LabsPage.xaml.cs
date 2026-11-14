using HVLab.Helpers;
using HVLab.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab.Views;

/// <summary>Lab blueprints: the list of the Blueprints folder and the graphical editor of one lab.</summary>
public sealed partial class LabsPage : Page
{
    public LabsViewModel ViewModel { get; } = App.GetService<LabsViewModel>();

    public LabsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();   // also opens a blueprint the VM list just exported
    }

    // Explorer and Notepad run as the user, not elevated
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = ViewModel.BlueprintsFolder;
        Directory.CreateDirectory(folder);
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        UnelevatedLauncher.Start(explorer, $"\"{folder}\"", explorer, $"\"{folder}\"");
    }

    /// <summary>The JSON behind the editor, for those who prefer the text (reload the page after editing it).</summary>
    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CurrentPath is not { } file) return;
        var notepad = UnelevatedLauncher.System32("notepad.exe");
        UnelevatedLauncher.Start(notepad, $"\"{file}\"", notepad, $"\"{file}\"");
    }
}
