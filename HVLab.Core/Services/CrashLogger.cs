namespace HVLab.Services;

/// <summary>
/// Minimal file logger for unhandled exceptions: %LocalAppData%\HV-LAB\logs\hvlab-yyyyMMdd.log.
/// </summary>
public static class CrashLogger
{
    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HV-LAB", "logs");

    private static readonly object Gate = new();

    public static void Log(string source, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogFolder);
                var file = Path.Combine(LogFolder, $"hvlab-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Hooks the process-wide and task-scheduler exception events.</summary>
    public static void RegisterGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}
