using HVLab.Services;
using Microsoft.UI.Xaml;

namespace HVLab;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        // Load persisted settings synchronously before any VM/service reads them
        AppSettings.LoadAsync().GetAwaiter().GetResult();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainAppWindow = new MainWindow();
        MainAppWindow.Activate();
    }
}
