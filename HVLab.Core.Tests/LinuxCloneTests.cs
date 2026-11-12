using System.Text.RegularExpressions;
using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class CloudInitTests
{
    private static CloudInitConfig Config(StaticIpConfig? ip = null) => new()
    {
        HostName = "ALMA-01", InstanceId = "id-1", UserName = "labadmin",
        Password = "p\"a\\ss", TimeZone = "Romance Standard Time", Language = "fr-FR", StaticIp = ip,
    };

    [Fact]
    public void Dhcp_seed_has_meta_data_and_user_data_only()
    {
        var seed = CloudInitGenerator.Generate(Config());
        Assert.Equal(["meta-data", "user-data"], seed.Files().Select(f => f.Key));
        Assert.Contains("instance-id: \"id-1\"", seed.MetaData);
        Assert.Contains("local-hostname: alma-01", seed.MetaData);
        Assert.StartsWith("#cloud-config\n", seed.UserData);
        Assert.Contains("hostname: alma-01", seed.UserData);
        Assert.Contains("  - name: labadmin", seed.UserData);
        Assert.Contains("password: \"p\\\"a\\\\ss\"", seed.UserData);   // quote and backslash escaped
        Assert.Contains("timezone: Europe/Paris", seed.UserData);
        Assert.Contains("layout: fr", seed.UserData);
        Assert.DoesNotContain("ssh_authorized_keys", seed.UserData);
    }

    [Fact]
    public void Static_ip_seed_has_a_network_config_for_eth0()
    {
        var seed = CloudInitGenerator.Generate(Config(new StaticIpConfig("192.168.100.10", 24, "192.168.100.1", ["1.1.1.1"])));
        Assert.Equal(3, seed.Files().Count());
        Assert.Contains("  eth0:", seed.NetworkConfig);
        Assert.Contains("- 192.168.100.10/24", seed.NetworkConfig);
        Assert.Contains("via: 192.168.100.1", seed.NetworkConfig);
        Assert.Contains("- 1.1.1.1", seed.NetworkConfig);
    }

    [Fact]
    public void Ssh_key_is_added_when_given()
    {
        var c = Config(); c.SshPublicKey = "ssh-ed25519 AAAAC3Nza user@host";
        Assert.Contains("ssh_authorized_keys:\n      - \"ssh-ed25519 AAAAC3Nza user@host\"", CloudInitGenerator.Generate(c).UserData);
    }

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("en-US", "us")]
    [InlineData("en-GB", "gb")]
    [InlineData("de-DE", "de")]
    public void Keyboard_follows_the_language(string language, string layout)
        => Assert.Equal(layout, CloudInitGenerator.KeyboardLayout(language));
}

public class LinuxInputTests
{
    [Theory]
    [InlineData("labadmin", true)]
    [InlineData("lab_admin-2", true)]
    [InlineData("root", false)]
    [InlineData("LabAdmin", false)]
    [InlineData("2lab", false)]
    [InlineData("", false)]
    public void Linux_user_names(string name, bool valid)
        => Assert.Equal(valid, InputValidator.CheckLinuxUser(name) is null);

    [Theory]
    [InlineData("192.168.100.10", 24, "192.168.100.1", 2, null)]
    [InlineData("192.168.100.10", 24, "10.0.0.1", 1, "Val_GatewaySubnet")]
    [InlineData("192.168.100.250", 24, "192.168.100.1", 10, "Val_IpRange")]     // runs into the broadcast address
    [InlineData("192.168.100.1", 24, "192.168.100.2", 3, "Val_IpRange")]       // overlaps the gateway
    [InlineData("192.168.100.10", 31, "192.168.100.1", 1, "Val_Prefix")]
    [InlineData("192.168.100.300", 24, "192.168.100.1", 1, "Val_Ipv4")]
    public void Static_ip_plans(string first, int prefix, string gateway, int count, string? error)
        => Assert.Equal(error, InputValidator.CheckStaticIpPlan(first, prefix, gateway, count));

    [Fact]
    public void Dns_servers_are_split_on_commas_and_spaces()
    {
        Assert.Equal(["1.1.1.1", "8.8.8.8"], InputValidator.ParseDnsServers("1.1.1.1, 8.8.8.8"));
        Assert.Empty(InputValidator.ParseDnsServers("")!);
        Assert.Null(InputValidator.ParseDnsServers("dns.local"));
    }

    [Theory]
    [InlineData("ssh-ed25519 AAAA user", true)]
    [InlineData("", true)]
    [InlineData("AAAA", false)]
    [InlineData("ssh-rsa AAAA\nssh-rsa BBBB", false)]
    public void Ssh_keys(string key, bool valid) => Assert.Equal(valid, InputValidator.CheckSshPublicKey(key) is null);
}

public class LinuxBaseTests
{
    internal static string TempDir() => Directory.CreateTempSubdirectory("hvlab-linux-").FullName;

    [Theory]
    [InlineData("9.6", "9.6.0.0")]
    [InlineData("9", "9.0.0.0")]
    [InlineData("24.04.1", "24.04.1.0")]
    [InlineData("9.x", null)]
    [InlineData("", null)]
    public void Versions_are_padded_to_four_numbers(string version, string? expected)
        => Assert.Equal(expected, BaseVhdx.NormalizeVersion(version));

    [Fact]
    public void Linux_identifier_keeps_letters_and_digits()
        => Assert.Equal("LinuxAlmaLinux9", BaseVhdx.LinuxIdentifier("Alma Linux 9"));

    [Fact]
    public void Sidecar_gives_the_firmware_of_the_source_vm()
    {
        var path = Path.Combine(TempDir(), "BASE_LinuxAlmaLinux_9.6.0.0_40.vhdx");
        File.WriteAllBytes(path, []);
        new BaseImageInfo { OsFamily = "Linux", Generation = 2, SecureBoot = false,
                            SecureBootTemplate = BaseImageInfo.LinuxSecureBootTemplate }.Save(path);

        var b = BaseVhdx.FromFile(path);
        Assert.True(b.IsLinux);
        Assert.False(b.SecureBoot);
        Assert.Equal(2, b.Generation);
        Assert.Equal(BaseImageInfo.LinuxSecureBootTemplate, b.SecureBootTemplate);
    }

    [Fact]
    public void Linux_base_without_sidecar_still_uses_the_linux_template()
    {
        var b = BaseVhdx.FromFile(Path.Combine(TempDir(), "BASE_LinuxDebian_12.0.0.0_20.vhdx"));
        Assert.True(b.IsLinux);
        Assert.Equal(BaseImageInfo.LinuxSecureBootTemplate, b.SecureBootTemplate);
        Assert.False(BaseVhdx.FromFile(@"C:\x\BASE_WindowsServer2022Standard_10.0.20348.587_50.vhdx").IsLinux);
    }

    [Fact]
    public async Task Base_from_a_vm_is_copied_named_and_described()
    {
        var folder = TempDir();
        // Fake Convert-VHD: creates the destination file and returns the source VM's firmware
        var runner = new FakePowerShellRunner(script =>
        {
            var dest = Regex.Match(script, @"\$destPath = '([^']+)'");
            if (!dest.Success) return "";
            File.WriteAllBytes(dest.Groups[1].Value, []);
            return """{"Generation":2,"SecureBoot":true,"SecureBootTemplate":"MicrosoftUEFICertificateAuthority","SizeGB":40}""";
        });
        var hv = new FakeHyperVService { Vms = [new VirtualMachine { Name = "ALMA-TPL", State = "Off" }] };
        var vm = new BaseVhdxViewModel(new VhdxService(runner), new FakeDialogService(), hv)
        {
            BaseFolder = folder, SourceVm = "ALMA-TPL", SourceFamily = "Linux",
            SourceDistribution = "AlmaLinux", SourceVersion = "9.6",
        };
        await vm.CreateBaseFromVmAsync();

        var path = Path.Combine(folder, "BASE_LinuxAlmaLinux_9.6.0.0_40.vhdx");
        Assert.True(File.Exists(path), vm.ErrorMessage);
        Assert.Empty(Directory.GetFiles(folder, "~building_*"));
        var b = Assert.Single(vm.BaseVhdxList);
        Assert.True(b.IsLinux);
        Assert.Equal("ALMA-TPL", b.Info!.SourceVm);
        Assert.Equal(BaseImageInfo.LinuxSecureBootTemplate, b.SecureBootTemplate);
    }

    [Fact]
    public async Task Linux_clones_get_a_seed_an_incremented_ip_and_the_uefi_template()
    {
        var path = Path.Combine(TempDir(), "BASE_LinuxAlmaLinux_9.6.0.0_40.vhdx");
        File.WriteAllBytes(path, []);
        new BaseImageInfo { OsFamily = "Linux", SecureBootTemplate = BaseImageInfo.LinuxSecureBootTemplate }.Save(path);

        var hv = new FakeHyperVService();
        var vm = new CreateVmViewModel(hv, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            SelectedBaseVhdx = path, SelectedSwitch = "NAT", AdminPassword = "Lab#2026",
            BulkMode = true, BulkPrefix = "ALMA", BulkCount = 2,
            UseStaticIp = true, IpAddress = "192.168.100.10", PrefixLength = 24, Gateway = "192.168.100.1", DnsServers = "1.1.1.1",
        };
        Assert.True(vm.IsLinuxBase);
        await vm.CreateVmAsync();

        Assert.Equal(["ALMA-01", "ALMA-02"], hv.CreatedFirmware.Keys.Order());
        var (template, seed1) = hv.CreatedFirmware["ALMA-01"];
        var seed2 = hv.CreatedFirmware["ALMA-02"].Seed;
        Assert.Equal(BaseImageInfo.LinuxSecureBootTemplate, template);
        Assert.Contains("local-hostname: alma-01", seed1!.MetaData);
        Assert.Contains("- 192.168.100.10/24", seed1.NetworkConfig);
        Assert.Contains("- 192.168.100.11/24", seed2!.NetworkConfig);
        Assert.NotEqual(seed1.MetaData, seed2.MetaData);   // distinct instance-id and host name
        Assert.Contains("labadmin", hv.CreatedNotes["ALMA-02"]);
        Assert.Contains("Lab#2026", hv.CreatedNotes["ALMA-02"]);
        Assert.Contains("192.168.100.11", hv.CreatedNotes["ALMA-02"]);
    }

    [Fact]
    public async Task Seed_files_are_embedded_and_written_to_a_cidata_disk()
    {
        var runner = new FakePowerShellRunner(_ => "");
        var seed = new CloudInitSeed("instance-id: x\n", "#cloud-config\npassword: \"Lab#2026\"\n", null);
        await new HyperVService(runner).CreateVMWithDifferencingDiskAsync("ALMA-01", @"C:\Base\BASE_LinuxAlma_9.6.0.0_40.vhdx",
            "NAT", 2048, 2, 2, @"C:\VMs", secureBootTemplate: BaseImageInfo.LinuxSecureBootTemplate, cloudInit: seed);
        var script = runner.Scripts.Single();
        Assert.Contains("-NewFileSystemLabel 'CIDATA'", script);
        Assert.Contains("$sbTemplate   = 'MicrosoftUEFICertificateAuthority'", script);
        Assert.Contains($"'user-data' = {HyperVService.PsLiteral(seed.UserData)}", script);
        Assert.DoesNotContain("Lab#2026", script);
    }
}
