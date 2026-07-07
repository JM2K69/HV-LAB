using HVLab.Services;
using Microsoft.UI.Xaml;

namespace HVLab;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        // Load persisted settings before any ViewModel reads them.
        AppSettings.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();

        // Apply persisted theme immediately after the window is live
        if (MainAppWindow.Content is FrameworkElement root)
            ThemeService.ApplyCurrent(root);
    }
}
