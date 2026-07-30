using HVLab.Services;
using HVLab.Views;
using Microsoft.UI.Xaml;

namespace HVLab;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        AppSettings.Load();
        LocalizationService.Instance.SetLanguage(AppSettings.Current.Language);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Afficher le splash immédiatement
        var splash = new SplashWindow();
        splash.Activate();

        // Pré-charger les données en tâche de fond puis ouvrir la fenêtre principale
        _ = StartupAsync(splash);
    }

    private async Task StartupAsync(SplashWindow splash)
    {
        var loc = LocalizationService.Instance;

        splash.SetStatus(loc["Splash_LoadingHyperV"]);
        await AppStartupCache.Instance.LoadAsync(AppSettings.Current.BaseImagesFolder);

        splash.SetStatus(loc["Splash_Starting"]);

        // Petite pause pour que le message soit visible
        await Task.Delay(300);

        // Ouvrir la fenêtre principale sur le thread UI
        splash.DispatcherQueue.TryEnqueue(() =>
        {
            MainAppWindow = new MainWindow();
            MainAppWindow.Activate();

            if (MainAppWindow.Content is FrameworkElement root)
                ThemeService.ApplyCurrent(root);

            splash.Close();
        });
    }
}
