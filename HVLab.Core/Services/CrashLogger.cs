namespace HVLab.Services;

/// <summary>
/// Minimal file logger for unhandled exceptions: %LocalAppData%\HV-LAB\logs\hvlab-yyyyMMdd.log.
/// </summary>
public static class CrashLogger
{
    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HV-LAB", "logs");

    private static readonly object Gate = new();

    /// <summary>Off in the unit tests, so they never write into the user's real log folder.</summary>
    public static bool Enabled { get; set; } = true;

    public static void Log(string source, Exception? ex)
    {
        if (!Enabled) return;
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

    /// <summary>
    /// One line per Hyper-V operation in operations-yyyyMMdd.log: name, duration, result. Never the script body
    /// (it can hold passwords): only the operation name and, on failure, the error the user saw.
    /// </summary>
    public static void Operation(string name, TimeSpan duration, Exception? error = null)
    {
        if (!Enabled) return;
        try
        {
            var result = error is null ? "OK" : $"FAILED {error.GetType().Name}: {error.Message.ReplaceLineEndings(" ")}";
            lock (Gate)
            {
                Directory.CreateDirectory(LogFolder);
                var file = Path.Combine(LogFolder, $"operations-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {name} {duration.TotalSeconds:0.0} s {result}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Deletes the log files older than <paramref name="days"/> days.</summary>
    public static void Prune(int days = 30)
    {
        try
        {
            if (!Directory.Exists(LogFolder)) return;
            var limit = DateTime.Now.AddDays(-days);
            foreach (var file in new DirectoryInfo(LogFolder).EnumerateFiles("*.log").Where(f => f.LastWriteTime < limit))
                file.Delete();
        }
        catch { /* best effort */ }
    }

    /// <summary>Hooks the process-wide and task-scheduler exception events.</summary>
    public static void RegisterGlobalHandlers()
    {
        Prune();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}
