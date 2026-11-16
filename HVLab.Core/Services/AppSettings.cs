using System.Text.Json;

namespace HVLab.Services;

/// <summary>
/// Persistent application settings stored at %ProgramData%\HV-LAB\settings\&lt;account SID&gt;.json (<see cref="AppData"/>).
/// Access via <see cref="Current"/>; call <see cref="SaveAsync"/> after any change.
/// </summary>
public sealed class AppSettings
{
    // ─── File location ────────────────────────────────────────────────────────

    private static string SettingsFile => AppData.SettingsFile;
    private static string SettingsDir  => Path.GetDirectoryName(SettingsFile)!;

    // ─── Singleton ────────────────────────────────────────────────────────────

    private static AppSettings? _current;

    /// <summary>Loaded singleton — call <see cref="LoadAsync"/> once at startup.</summary>
    public static AppSettings Current => _current ?? new AppSettings();

    // ─── Persisted properties ─────────────────────────────────────────────────

    /// <summary>Root folder where BASE_*.vhdx files are stored.</summary>
    public string BaseImagesFolder { get; set; } = @"C:\HV-LAB\BaseImages";

    /// <summary>Root folder where VM differencing disks and config live.</summary>
    public string VmsFolder { get; set; } = @"C:\HV-LAB\VMs";

    // Main window placement (physical pixels), restored at startup
    public int?  WindowX         { get; set; }
    public int?  WindowY         { get; set; }
    public int?  WindowWidth     { get; set; }
    public int?  WindowHeight    { get; set; }
    public bool  WindowMaximized { get; set; }

    /// <summary>
    /// Application theme preference: "System" (default), "Light", or "Dark".
    /// </summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// Interface language: "fr" (default) or "en".
    /// </summary>
    public string Language { get; set; } = "fr";

    /// <summary>
    /// Off by default (a lab: the password in clear in Hyper-V Manager is handy). On: the password written into the
    /// notes of new VMs is DPAPI-protected, readable back by this Windows account on this host only.
    /// </summary>
    public bool ProtectNotesPassword { get; set; }

    // ─── Load / Save ──────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronous load — safe to call from constructors and STA threads.
    /// </summary>
    public static void Load()
    {
        if (File.Exists(SettingsFile))
        {
            _current = ReadFile(SettingsFile) ?? new AppSettings();
            return;
        }
        // First start of this version: the settings of the former, user-writable folder, moved once
        _current = ReadFile(AppData.LegacySettingsFile) ?? new AppSettings();
        try { _current.Save(); }
        catch (Exception) { /* not elevated: kept in memory for this session */ }
    }

    /// <summary>Load settings from disk (async variant for non-UI contexts).</summary>
    public static Task LoadAsync() => Task.Run(Load);

    /// <summary>The settings of <paramref name="path"/>, or null when the file is missing or unreadable.</summary>
    internal static AppSettings? ReadFile(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>Persist current settings to disk (synchronous).</summary>
    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomically(json);
    }

    /// <summary>Persist current settings to disk (async).</summary>
    public async Task SaveAsync()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        await Task.Run(() => WriteAtomically(json)).ConfigureAwait(false);
    }

    // Write to a temp file then replace: a crash mid-write never leaves a truncated settings.json
    private static void WriteAtomically(string json)
    {
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, SettingsFile, overwrite: true);
    }
}
