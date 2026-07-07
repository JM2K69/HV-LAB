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
        // Must be synchronous — the constructor runs on the dispatcher thread;
        // blocking on an async task here causes a deadlock.
        AppSettings.Load();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}
