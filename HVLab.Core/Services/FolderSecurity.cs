using System.Security.AccessControl;
using System.Security.Principal;

namespace HVLab.Services;

/// <summary>
/// A folder created under C:\ inherits "Authenticated Users: Modify": any standard user could replace a base image
/// that every clone then boots from, with the administrator's VMs. The base images folder is made writable by
/// administrators only; everyone keeps read access (browsing, and the VM worker processes reading the parents).
/// </summary>
public static class FolderSecurity
{
    private static readonly SecurityIdentifier Administrators     = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem        = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsers = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier HyperVAdmins       = new("S-1-5-32-578");   // Hyper-V Administrators
    private static readonly SecurityIdentifier VirtualMachines    = new("S-1-5-83-0");     // NT VIRTUAL MACHINE\Virtual Machines
    private static readonly SecurityIdentifier OwnerRights        = new("S-1-3-4");        // limits what the owner may do

    /// <summary>The rules applied by <see cref="Harden(string)"/> to the base images folder: (account, rights).</summary>
    public static IReadOnlyList<(SecurityIdentifier Account, FileSystemRights Rights)> Rules { get; } =
    [
        (Administrators,     FileSystemRights.FullControl),
        (LocalSystem,        FileSystemRights.FullControl),
        (HyperVAdmins,       FileSystemRights.FullControl),
        (VirtualMachines,    FileSystemRights.ReadAndExecute),
        (AuthenticatedUsers, FileSystemRights.ReadAndExecute),
    ];

    /// <summary>
    /// The rules of the app's own data folder (settings, logs): administrators write, users read the logs. OWNER
    /// RIGHTS read-only: whoever owns a file there cannot rewrite its permissions (on a host where objects created by
    /// administrators belong to their creator, the creator would otherwise keep that right without elevation).
    /// </summary>
    public static IReadOnlyList<(SecurityIdentifier Account, FileSystemRights Rights)> DataRules { get; } =
    [
        (Administrators,     FileSystemRights.FullControl),
        (LocalSystem,        FileSystemRights.FullControl),
        (OwnerRights,        FileSystemRights.ReadAndExecute),
        (AuthenticatedUsers, FileSystemRights.ReadAndExecute),
    ];

    /// <summary>
    /// True when <paramref name="path"/> may be hardened: a folder of base images (it holds BASE_*.vhdx files, or
    /// nothing yet), never a drive root, a folder inside Windows or Program Files, nor a profile or one of its known
    /// folders. HV-LAB never changes the permissions of a folder it does not own: the path comes from the settings,
    /// and a wrong one (C:\Windows\System32, Documents, the folder above the VMs) must be left alone.
    /// </summary>
    public static bool CanHarden(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (full.Length <= 3) return false;
        static string Known(Environment.SpecialFolder f) => Environment.GetFolderPath(f).TrimEnd('\\');
        string[] system = [Known(Environment.SpecialFolder.Windows), Known(Environment.SpecialFolder.ProgramFiles),
                           Known(Environment.SpecialFolder.ProgramFilesX86)];
        if (system.Any(s => s.Length > 0 && (Same(full, s) || full.StartsWith(s + '\\', StringComparison.OrdinalIgnoreCase))))
            return false;
        var profile = Known(Environment.SpecialFolder.UserProfile);
        string[] personal = [profile, Path.GetDirectoryName(profile) ?? "", Path.Combine(profile, "Downloads"),
                             Known(Environment.SpecialFolder.CommonApplicationData), Known(Environment.SpecialFolder.Desktop),
                             Known(Environment.SpecialFolder.MyDocuments), Known(Environment.SpecialFolder.MyPictures),
                             Known(Environment.SpecialFolder.MyVideos), Known(Environment.SpecialFolder.MyMusic)];
        if (personal.Any(s => s.Length > 0 && Same(full, s))) return false;
        var entries = Directory.EnumerateFileSystemEntries(full).Select(Path.GetFileName).ToList();
        return entries.Count == 0
            || entries.Any(n => n!.StartsWith("BASE_", StringComparison.OrdinalIgnoreCase)
                             && n.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase));
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Already hardened: owned by Administrators or SYSTEM, inheritance off, nobody but the admin accounts may write.</summary>
    public static bool IsHardened(string path)
    {
        var acl = new DirectoryInfo(path).GetAccessControl();
        if (!acl.AreAccessRulesProtected) return false;
        if (acl.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !(owner.Equals(Administrators) || owner.Equals(LocalSystem))) return false;
        var writers = new[] { Administrators, LocalSystem, HyperVAdmins };
        const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
                                     | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & write) != 0
                && !writers.Contains((SecurityIdentifier)rule.IdentityReference))
                return false;
        return true;
    }

    /// <summary>Owner Administrators, inheritance off, <see cref="Rules"/> for the folder and everything in it.</summary>
    public static void Harden(string path) => Harden(path, Rules);

    /// <summary>Owner Administrators, inheritance off, <paramref name="rules"/> for the folder and everything in it.</summary>
    public static void Harden(string path, IReadOnlyList<(SecurityIdentifier Account, FileSystemRights Rights)> rules)
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var (account, rights) in rules)
            security.AddAccessRule(new FileSystemAccessRule(account, rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    /// <summary>Called at startup (the app is elevated): hardens the base images folder once; never throws.</summary>
    public static void HardenBaseImagesFolder(string path)
    {
        try
        {
            if (CanHarden(path) && !IsHardened(path)) Harden(path);
        }
        catch (Exception ex) { CrashLogger.Log($"Permissions of {path}", ex); }
    }
}
