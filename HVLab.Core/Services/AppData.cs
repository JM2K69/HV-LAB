using System.Security.Principal;

namespace HVLab.Services;

/// <summary>
/// %ProgramData%\HV-LAB: the settings and logs of the app, which always runs elevated. Only administrators may write
/// there, so a program the user runs without elevation can neither change what the elevated app reads (the folders it
/// works in) nor turn its log folder into a link to a system folder. Settings stay per Windows account: one file per
/// account SID.
/// </summary>
public static class AppData
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HV-LAB");

    public static string LogFolder { get; } = Path.Combine(Folder, "logs");

    public static string SettingsFile { get; } = Path.Combine(Folder, "settings", $"{CurrentSid()}.json");

    /// <summary>The settings of versions up to 2.0 (%LocalAppData%\HV-LAB, writable without elevation): read once to migrate.</summary>
    public static string LegacySettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HV-LAB", "settings.json");

    private static string CurrentSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value ?? "default"; }
        catch (Exception) { return "default"; }
    }

    /// <summary>
    /// Called first at startup, before anything is read or logged there; never throws (the app then runs on its
    /// defaults, and its logs stay unwritten).
    /// </summary>
    public static void Secure()
    {
        try { Secure(Folder); }
        catch (Exception) { /* not elevated, or a disk error: nothing to log into yet */ }
    }

    /// <summary>
    /// Creates <paramref name="folder"/> and makes it writable by administrators only. A folder hardened before is
    /// trusted as is: only administrators could write in it since. Otherwise (first run of this version, or a folder
    /// someone else created first) the folders the app reads and writes are emptied, and any link at the top level is
    /// removed, never followed.
    /// </summary>
    internal static void Secure(string folder)
    {
        var root = new DirectoryInfo(folder);
        if (root.Exists && root.Attributes.HasFlag(FileAttributes.ReparsePoint)) root.Delete();   // the link, not its target
        Directory.CreateDirectory(folder);
        if (FolderSecurity.IsHardened(folder)) return;

        FolderSecurity.Harden(folder, FolderSecurity.DataRules);
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                entry.Delete();
            else if (entry is DirectoryInfo dir && dir.Name is "settings" or "logs")
                dir.Delete(recursive: true);   // a link inside is removed as a link: its target is never entered
        }
    }
}
