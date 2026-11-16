using HVLab.Services;

namespace HVLab.Core.Tests;

public class SecurityTests
{
    [Fact]
    public void Sha512_crypt_matches_the_reference_vector()
        // Test vector of the SHA-crypt specification (Ulrich Drepper), as produced by glibc crypt()
        => Assert.Equal("$6$saltstring$svn8UoSVapNtMuq1ukKS4tPQd8iKwSMHWjl/O817G3uBnIFNjnQJuesI68u4OTLiBFdcbYEdFCoEOfaS35inz1",
                        Sha512Crypt.Hash("Hello world!", "saltstring"));

    [Fact]
    public void Sha512_crypt_uses_a_random_16_character_salt()
    {
        var a = Sha512Crypt.Hash("Lab#2026");
        var b = Sha512Crypt.Hash("Lab#2026");
        Assert.NotEqual(a, b);
        Assert.Matches(@"^\$6\$[./0-9A-Za-z]{16}\$[./0-9A-Za-z]{86}$", a);
    }

    [Fact]
    public void The_cloud_init_seed_never_holds_the_password_in_clear()
    {
        var seed = CloudInitGenerator.Generate(new CloudInitConfig { HostName = "lab-01", Password = "Lab#2026-secret" });
        Assert.DoesNotContain("Lab#2026-secret", seed.UserData);
        Assert.Matches(@"hashed_passwd: ""\$6\$", seed.UserData);
    }

    [Fact]
    public void A_protected_password_reads_back_for_this_account_only()
    {
        var notes = VmNotes.Build(DateTime.Now, "Administrator", "P@ss w0rd ", protect: true);
        Assert.DoesNotContain("P@ss w0rd", notes);
        Assert.Contains("(DPAPI)", notes);
        Assert.Equal("P@ss w0rd ", VmNotes.Read(notes).Password);          // same account: readable
        Assert.Equal("", VmNotes.Read(notes.Replace(notes.Split(' ').Last(), "Zm9v")).Password);   // not DPAPI data
    }

    [Fact]
    public void Notes_keep_the_password_in_clear_by_default()
    {
        var notes = VmNotes.Build(DateTime.Now, "Administrator", "Lab#2026", protect: false);
        Assert.Equal("Lab#2026", VmNotes.Read(notes).Password);
        Assert.Contains("Lab#2026", notes);
    }

    [Fact]
    public void Only_dedicated_folders_can_be_hardened()
    {
        Assert.False(FolderSecurity.CanHarden(@"C:\"));
        Assert.False(FolderSecurity.CanHarden(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.False(FolderSecurity.CanHarden(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.False(FolderSecurity.CanHarden(@"Z:\does\not\exist"));
        var dir = Directory.CreateTempSubdirectory("hvlab-acl-").FullName;
        try { Assert.True(FolderSecurity.CanHarden(dir)); }   // a new, empty folder
        finally { Directory.Delete(dir); }
    }

    [Fact]
    public void A_wrong_base_images_folder_is_never_hardened()
    {
        // Inside Windows or Program Files, or a profile folder: whatever the settings say
        Assert.False(FolderSecurity.CanHarden(Environment.GetFolderPath(Environment.SpecialFolder.System)));
        Assert.False(FolderSecurity.CanHarden(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files")));
        Assert.False(FolderSecurity.CanHarden(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.False(FolderSecurity.CanHarden(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));

        // A folder of other things (the parent of the VMs, say) is left alone; one of base images is hardened
        var dir = Directory.CreateTempSubdirectory("hvlab-acl-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "notes.txt"), [1]);
            Assert.False(FolderSecurity.CanHarden(dir));
            File.WriteAllBytes(Path.Combine(dir, "BASE_WindowsServer2025Standard_10.0.26100.1_50.vhdx"), [1]);
            Assert.True(FolderSecurity.CanHarden(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Hardened_folders_let_only_admins_write()
    {
        Assert.Contains(FolderSecurity.Rules, r => r.Account.Value == "S-1-5-11"
                        && r.Rights == System.Security.AccessControl.FileSystemRights.ReadAndExecute);   // users: read only
        Assert.Contains(FolderSecurity.Rules, r => r.Account.Value == "S-1-5-83-0");                    // VM workers read
    }
}
