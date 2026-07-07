using System.Text.Json;

namespace HVLab.Services;

/// <summary>
/// Persistent application settings stored at %LocalAppData%\HV-LAB\settings.json.
/// Access via <see cref="Current"/>; call <see cref="SaveAsync"/> after any change.
/// </summary>
public sealed class AppSettings
{
    // ─── File location ────────────────────────────────────────────────────────

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HV-LAB");

    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "settings.json");

    // ─── Singleton ────────────────────────────────────────────────────────────

    private static AppSettings? _current;

    /// <summary>Loaded singleton — call <see cref="LoadAsync"/> once at startup.</summary>
    public static AppSettings Current => _current ?? new AppSettings();

    // ─── Persisted properties ─────────────────────────────────────────────────

    /// <summary>Root folder where BASE_*.vhdx files are stored.</summary>
    public string BaseImagesFolder { get; set; } = @"C:\HV-LAB\BaseImages";

    /// <summary>Root folder where VM differencing disks and config live.</summary>
    public string VmsFolder { get; set; } = @"C:\HV-LAB\VMs";

    /// <summary>Default Windows PowerShell path used for Hyper-V scripts.</summary>
    public string PowerShellPath { get; set; } =
        @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

    // ─── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronous load — safe to call from constructors and STA threads.
    /// </summary>
    public static void Load()
    {
        if (!File.Exists(SettingsFile))
        {
            _current = new AppSettings();
            _current.Save();
            return;
        }
        try
        {
            var json = File.ReadAllText(SettingsFile);
            _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            _current = new AppSettings();
        }
    }

    /// <summary>Load settings from disk (async variant for non-UI contexts).</summary>
    public static async Task LoadAsync()
    {
        if (!File.Exists(SettingsFile))
        {
            _current = new AppSettings();
            await _current.SaveAsync().ConfigureAwait(false);
            return;
        }
        try
        {
            var json = await File.ReadAllTextAsync(SettingsFile).ConfigureAwait(false);
            _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            _current = new AppSettings();
        }
    }

    /// <summary>Persist current settings to disk (synchronous).</summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
    }

    /// <summary>Persist current settings to disk (async).</summary>
    public async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SettingsFile, json).ConfigureAwait(false);
    }
}
