using HVLab.Services;
using HVLab.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace HVLab;

public partial class App : Application
{
    public static MainWindow? MainAppWindow { get; private set; }

    /// <summary>Application services: Core services/view models + UI-layer implementations.</summary>
    public static IServiceProvider Services { get; } = new ServiceCollection()
        .AddHVLabCore()
        .AddSingleton<IThemeService, WindowThemeService>()
        .AddSingleton<IDialogService, DialogService>()
        .BuildServiceProvider();

    public static T GetService<T>() where T : notnull => Services.GetRequiredService<T>();

    public App()
    {
        CrashLogger.RegisterGlobalHandlers();
        UnhandledException += (_, e) =>
        {
            CrashLogger.Log("Application.UnhandledException", e.Exception);
            e.Handled = true; // keep the app alive; the error is logged
        };

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
        try
        {
            splash.SetStatus(loc["Splash_LoadingHyperV"]);
            await GetService<AppStartupCache>().LoadAsync(AppSettings.Current.BaseImagesFolder);

            splash.SetStatus(loc["Splash_Starting"]);

            // Petite pause pour que le message soit visible
            await Task.Delay(300);
        }
        catch (Exception ex)
        {
            // Le cache capture déjà les erreurs Hyper-V ; on continue vers la fenêtre principale
            CrashLogger.Log("StartupAsync", ex);
        }

        // Ouvrir la fenêtre principale sur le thread UI
        splash.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                MainAppWindow = new MainWindow();
                MainAppWindow.Activate();

                if (MainAppWindow.Content is FrameworkElement root)
                    ThemeService.ApplyCurrent(root);
            }
            catch (Exception ex)
            {
                CrashLogger.Log("MainWindow startup", ex);
                _ = NativeMethods.MessageBox(IntPtr.Zero,
                    $"{ex.Message}\n\nLog : {CrashLogger.LogFolder}", "HV-LAB — Erreur de démarrage", 0x10);
                Exit();
            }
            finally
            {
                splash.Close();
            }
        });
    }
}
