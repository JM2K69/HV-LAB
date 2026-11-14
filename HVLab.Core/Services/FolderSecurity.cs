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

    /// <summary>The rules applied by <see cref="Harden"/>: (account, rights).</summary>
    public static IReadOnlyList<(SecurityIdentifier Account, FileSystemRights Rights)> Rules { get; } =
    [
        (Administrators,     FileSystemRights.FullControl),
        (LocalSystem,        FileSystemRights.FullControl),
        (HyperVAdmins,       FileSystemRights.FullControl),
        (VirtualMachines,    FileSystemRights.ReadAndExecute),
        (AuthenticatedUsers, FileSystemRights.ReadAndExecute),
    ];

    /// <summary>
    /// True when <paramref name="path"/> may be hardened: an existing folder, not a drive root nor a system folder
    /// (HV-LAB never changes the permissions of a folder it does not own).
    /// </summary>
    public static bool CanHarden(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        var full = Path.GetFullPath(path).TrimEnd('\\');
        if (full.Length <= 3) return false;
        string[] system = [Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                           Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                           Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                           Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)];
        return !system.Any(s => s.Length > 0 && string.Equals(full, s.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Already hardened: inheritance off, and nobody but the admin accounts may write.</summary>
    public static bool IsHardened(string path)
    {
        var acl = new DirectoryInfo(path).GetAccessControl();
        if (!acl.AreAccessRulesProtected) return false;
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
    public static void Harden(string path)
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var (account, rights) in Rules)
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
