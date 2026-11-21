using HVLab.Models;
using HVLab.Services;

namespace HVLab.Core.Tests;

public class LabBlueprintTests
{
    private const string Json = """
        // A domain controller, a member server and a Linux web server on a NAT
        {
          "name": "LAB-AD",
          "switch": "LAB-NAT",
          "nat": { "gateway": "192.168.100.1", "prefixLength": 24 },
          "password": "Lab#2026",
          "domain": { "name": "lab.local", "user": "Administrator" },
          "vms": [
            { "name": "DC01",  "base": "WindowsServer2025*", "memoryMB": 4096, "ip": "192.168.100.10", "dns": "127.0.0.1" },
            { "name": "SRV01", "base": "WindowsServer2025*", "ip": "192.168.100.11", "dns": "192.168.100.10", "join": true },
            { "name": "WEB01", "base": "LinuxAlma*", "ip": "192.168.100.20", "user": "web" },
          ]
        }
        """;

    private static string BaseFolder()
    {
        var dir = Directory.CreateTempSubdirectory("hvlab-lab-").FullName;
        foreach (var name in new[] { "BASE_WindowsServer2025Standard_10.0.26100.1_50", "BASE_WindowsServer2025Standard_10.0.26100.4652_50",
                                     "BASE_LinuxAlma_9.6.0.0_40", "BASE_WindowsServer2022Standard_10.0.20348.587_50" })
            File.WriteAllBytes(Path.Combine(dir, name + ".vhdx"), [1]);
        return dir;
    }

    private static (LabBlueprintService Labs, FakeHyperVService Hv, FakeNatService Nat) Service(string bases)
    {
        var hv = new FakeHyperVService();
        var nat = new FakeNatService();
        var labs = new LabBlueprintService(hv, nat, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            BaseFolder = () => bases, VmsFolder = () => @"D:\VMs",
        };
        return (labs, hv, nat);
    }

    [Fact]
    public void A_commented_blueprint_parses_and_validates()
    {
        var lab = LabBlueprint.Parse(Json);
        Assert.Null(lab.Validate());
        Assert.Equal(3, lab.Vms.Count);
        Assert.True(lab.Vms[1].Join);
        Assert.Equal(2048, lab.Vms[1].MemoryMB);   // default size
    }

    [Theory]
    [InlineData("\"password\": \"Lab#2026\",", "\"password\": \"\",", "Val_PasswordEmpty")]
    [InlineData("\"ip\": \"192.168.100.11\"", "\"ip\": \"192.168.100.999\"", "LAB_ErrIp")]
    [InlineData("\"name\": \"SRV01\"", "\"name\": \"DC01\"", "LAB_ErrDuplicate")]
    public void Blueprint_errors_are_reported(string from, string to, string key)
    {
        var error = LabBlueprint.Parse(Json.Replace(from, to)).Validate();
        Assert.NotNull(error);
        Assert.StartsWith(LocalizationService.T(key, "DC01").Split('{')[0][..10], error);
    }

    [Fact]
    public void The_newest_matching_base_is_picked()
    {
        var bases = Directory.GetFiles(BaseFolder(), "*.vhdx").Select(BaseVhdx.FromFile).ToList();
        Assert.Equal("10.0.26100.4652", LabBlueprintService.PickBase(bases, "WindowsServer2025*")!.OsVersion);
        Assert.Equal("LinuxAlma", LabBlueprintService.PickBase(bases, "linuxalma*")!.OsIdentifier);
        Assert.Null(LabBlueprintService.PickBase(bases, "Ubuntu*"));
    }

    [Fact]
    public async Task The_plan_creates_only_what_is_missing()
    {
        var (labs, hv, _) = Service(BaseFolder());
        hv.Switches = [new VirtualSwitch { Name = "LAB-NAT", SwitchType = "Internal" }];
        hv.Vms = [new VirtualMachine { Id = Guid.NewGuid().ToString(), Name = "DC01", State = "Off" }];
        var plan = await labs.PlanAsync(LabBlueprint.Parse(Json));
        Assert.Equal(LabBlueprintService.Exists, plan.Single(s => s.Kind == "switch").Action);
        Assert.DoesNotContain(plan, s => s.Kind == "nat");                      // the switch exists: its NAT is kept as is
        Assert.Equal(LabBlueprintService.Exists, plan.Single(s => s.Name == "DC01").Action);
        Assert.Equal(LabBlueprintService.Create, plan.Single(s => s.Name == "SRV01").Action);
    }

    [Fact]
    public async Task A_missing_base_blocks_the_deployment()
    {
        var (labs, _, _) = Service(BaseFolder());
        var lab = LabBlueprint.Parse(Json.Replace("LinuxAlma*", "Ubuntu*"));
        var plan = await labs.PlanAsync(lab);
        Assert.Equal(LabBlueprintService.Missing, plan.Single(s => s.Name == "WEB01").Action);
        await Assert.ThrowsAsync<InvalidOperationException>(() => labs.DeployAsync(lab));
    }

    [Fact]
    public async Task Deploying_creates_the_switch_the_nat_and_each_vm_then_groups_them()
    {
        var (labs, hv, nat) = Service(BaseFolder());
        await labs.DeployAsync(LabBlueprint.Parse(Json));

        Assert.Contains("internal-switch LAB-NAT", hv.Calls);
        Assert.Equal(["LAB-AD-NAT LAB-NAT 192.168.100.1/24"], nat.Created);
        Assert.Contains("192.168.100.10/24", hv.CreatedAnswers["DC01"]);
        Assert.Contains("<JoinDomain>lab.local</JoinDomain>", hv.CreatedAnswers["SRV01"]);
        Assert.DoesNotContain("JoinDomain", hv.CreatedAnswers["DC01"]);          // only the VMs with "join"
        Assert.Contains("192.168.100.10", hv.CreatedAnswers["SRV01"]);            // its DNS: the DC
        Assert.Null(hv.CreatedAnswers["WEB01"]);                                  // Linux: a cloud-init seed instead
        Assert.Equal("web", VmNotes.Read(hv.CreatedNotes["WEB01"]).Account);
        Assert.Contains("group LAB-AD DC01,SRV01,WEB01", hv.Calls);
    }

    [Fact]
    public async Task The_new_template_parses_uses_the_host_bases_and_waits_for_a_password()
    {
        var (labs, _, _) = Service(BaseFolder());
        var lab = LabBlueprint.Parse(await labs.NewTemplateAsync());   // comments and all
        Assert.Equal("WindowsServer2025Standard*", lab.Vms[0].Base);    // the newest server base of the folder
        Assert.Equal("LinuxAlma*", lab.Vms[2].Base);
        Assert.Equal(LocalizationService.T("Val_PasswordEmpty"), lab.Validate());   // not deployable as is
        lab.Password = "Lab#2026";
        Assert.Null(lab.Validate());
        Assert.Equal(3, (await labs.PlanAsync(lab)).Count(s => s.Action == LabBlueprintService.Create && s.Kind == "vm"));
    }

    [Fact]
    public async Task The_ad_member_template_is_a_dc_and_its_member_on_the_newest_server_base()
    {
        var (labs, _, _) = Service(BaseFolder());
        var lab = LabBlueprint.Parse(await labs.NewTemplateAsync(LabBlueprintService.TemplateAdMember));
        Assert.Equal(["DC01", "SRV01"], lab.Vms.Select(v => v.Name));
        Assert.True(lab.Vms[0].DomainController);
        Assert.True(lab.Vms[1].Join);
        Assert.All(lab.Vms, v => Assert.Equal("WindowsServer2025Standard*", v.Base));
        Assert.Equal("lab.local", lab.Domain!.Name);

        Assert.Equal(LocalizationService.T("Val_PasswordEmpty"), lab.Validate());   // not deployable as is
        lab.Password = "Lab#2026";
        Assert.Null(lab.Validate());
        var plan = await labs.PlanAsync(lab);
        Assert.Equal(2, plan.Count(s => s.Action == LabBlueprintService.Create && s.Kind == "vm"));
        Assert.Contains(plan, s => s.Kind == "domain" && s.Action == LabBlueprintService.Create);
    }

    [Fact]
    public void Blueprint_files_never_overwrite_one_another()
    {
        var vms = Directory.CreateTempSubdirectory("hvlab-bp-").FullName;
        var labs = new LabBlueprintService(new FakeHyperVService(), new FakeNatService(), new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            VmsFolder = () => vms,
        };
        var first = labs.FreePath("LAB:AD");
        File.WriteAllText(first, "{}");
        Assert.EndsWith("LAB-AD.json", first);                          // invalid file-name characters replaced
        Assert.EndsWith("LAB-AD-2.json", labs.FreePath("LAB:AD"));
    }

    [Fact]
    public async Task Exporting_one_vm_writes_its_blueprint_and_announces_the_file()
    {
        var vms = Directory.CreateTempSubdirectory("hvlab-bp-").FullName;
        var hv = new FakeHyperVService();
        var labs = new LabBlueprintService(hv, new FakeNatService(), new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            VmsFolder = () => vms, BaseFolder = () => vms,
        };
        var a = new VirtualMachine { Id = Guid.NewGuid().ToString(), Name = "WEB01", State = "Off", MemoryMB = 2048, ProcessorCount = 2 };
        hv.Vms = [a];
        var vm = new ViewModels.VirtualMachinesViewModel(hv, new FakeDialogService(), labs);
        string? written = null;
        vm.BlueprintWritten += (_, file) => written = file;
        await vm.RefreshAsync();
        await vm.ExportBlueprintAsync([vm.VirtualMachines[0]]);
        Assert.NotNull(written);
        Assert.Equal("WEB01", LabBlueprint.Parse(File.ReadAllText(written!)).Vms.Single().Name);
    }

    [Fact]
    public async Task An_export_keeps_the_startup_memory_of_a_running_vm()
    {
        var labs = new LabBlueprintService(new FakeHyperVService(), new FakeNatService(),
                                           new VhdxService(new FakePowerShellRunner(_ => "[]")));
        // Dynamic memory: the running VM was given 2048 MB and uses 2682 MB now
        var running = new VirtualMachine { Name = "SRV01", State = "Running", MemoryMB = 2682, StartupMemoryMB = 2048 };
        var bp = await labs.ExportAsync("LAB", [running]);
        Assert.Equal(2048, bp.Vms.Single().MemoryMB);
    }

    // ─── Domain controller ──────────────────────────────────────────────────

    // The DC is listed after a member on purpose: it is created and promoted first anyway
    private const string DcJson = """
        {
          "name": "LAB-AD", "switch": "LAB-NAT",
          "nat": { "gateway": "192.168.100.1", "prefixLength": 24 },
          "password": "Lab#2026",
          "domain": { "name": "corp.lab.local" },
          "vms": [
            { "name": "SRV01", "base": "WindowsServer2025*", "ip": "192.168.100.11", "join": true },
            { "name": "DC01",  "base": "WindowsServer2025*", "ip": "192.168.100.10", "domainController": true },
            { "name": "WEB01", "base": "LinuxAlma*", "ip": "192.168.100.20" },
          ]
        }
        """;

    private static (LabBlueprintService Labs, FakeHyperVService Hv) DcService(TimeSpan? setup = null)
    {
        var hv = new FakeHyperVService { AddCreatedVms = true };
        var bases = BaseFolder();
        var labs = new LabBlueprintService(hv, new FakeNatService(), new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            BaseFolder = () => bases, VmsFolder = () => @"D:\VMs",
            GuestPoll = TimeSpan.Zero, GuestSetupTimeout = setup ?? TimeSpan.FromMinutes(1), DirectoryTimeout = TimeSpan.FromMinutes(1),
        };
        return (labs, hv);
    }

    [Theory]
    [InlineData("\"join\": true", "\"domainController\": true", "LAB_ErrTwoDc")]
    [InlineData("\"domain\": { \"name\": \"corp.lab.local\" },", "", "LAB_ErrDcNoDomain")]
    [InlineData("\"ip\": \"192.168.100.10\", \"domainController\"", "\"domainController\"", "LAB_ErrDcIp")]
    [InlineData("\"ip\": \"192.168.100.10\", \"domainController\": true", "\"ip\": \"192.168.100.10\", \"domainController\": true, \"join\": true", "LAB_ErrDcJoin")]
    [InlineData("\"password\": \"Lab#2026\"", "\"password\": \"labpassword\"", "LAB_ErrDcPassword")]
    public void Domain_controller_errors_are_reported(string from, string to, string key)
    {
        var json = DcJson.Replace(from, to);
        Assert.NotEqual(DcJson, json);
        Assert.Equal(LocalizationService.T(key, "DC01"), LabBlueprint.Parse(json).Validate());
    }

    [Theory]
    [InlineData("Lab#2026", true)]
    [InlineData("Passw0rd", true)]
    [InlineData("labpassword", false)]
    [InlineData("Ab1!", false)]
    public void Domain_passwords_follow_the_windows_complexity_rule(string password, bool complex)
        => Assert.Equal(complex, LabBlueprint.IsComplex(password));

    [Theory]
    [InlineData("lab.local", "LAB")]
    [InlineData("corp.lab.local", "CORP")]
    [InlineData("averyverylongdomainname.lab", "AVERYVERYLONGDO")]
    public void The_netbios_name_is_the_first_label(string domain, string netbios)
        => Assert.Equal(netbios, LabBlueprintService.NetBiosName(domain));

    [Fact]
    public async Task The_plan_promotes_the_dc_on_a_windows_server_only()
    {
        var (labs, _) = DcService();
        var lab = LabBlueprint.Parse(DcJson);
        var domain = (await labs.PlanAsync(lab)).Single(s => s.Kind == "domain");
        Assert.Equal(("corp.lab.local", LabBlueprintService.Create), (domain.Name, domain.Action));

        lab.Vms[1].Base = "LinuxAlma*";
        Assert.Equal(LabBlueprintService.Missing, (await labs.PlanAsync(lab)).Single(s => s.Name == "DC01").Action);
    }

    [Fact]
    public async Task Deploying_promotes_the_dc_before_creating_the_members()
    {
        var (labs, hv) = DcService();
        var promoted = false; var adTries = 0;
        hv.Guest = (account, script) =>
        {
            if (script.Contains("DomainRole")) return promoted ? "5" : "2";      // 2: standalone server
            if (script.Contains("Install-ADDSForest")) promoted = true;
            if (script.Contains("Get-ADDomain"))
                return adTries++ == 0 ? throw new InvalidOperationException("restarting") : "corp.lab.local";
            return "";
        };
        await labs.DeployAsync(LabBlueprint.Parse(DcJson));

        int At(string start) => hv.Calls.FindIndex(c => c.StartsWith(start, StringComparison.Ordinal));
        Assert.True(At("create DC01") < At("power ID-DC01 Start"));
        Assert.True(At("power ID-DC01 Start") < At("guest ID-DC01 Administrator Install-WindowsFeature"));
        Assert.True(At("guest ID-DC01 Administrator Install-WindowsFeature") < At("guest ID-DC01 Administrator $safe"));
        Assert.True(At(@"guest ID-DC01 CORP\Administrator (Get-ADDomain") < At("create SRV01"));
        Assert.True(At("create SRV01") < At("create WEB01"));
        Assert.Contains(hv.Calls, c => c.Contains("-DomainName 'corp.lab.local' -DomainNetbiosName 'CORP'"));

        // The members join the new domain and use the DC as DNS server, the DC itself the NAT gateway
        Assert.Contains("<JoinDomain>corp.lab.local</JoinDomain>", hv.CreatedAnswers["SRV01"]);
        Assert.Contains("192.168.100.10", hv.CreatedAnswers["SRV01"]);
        Assert.DoesNotContain("JoinDomain", hv.CreatedAnswers["DC01"]);
        Assert.Contains("192.168.100.10", string.Concat(hv.CreatedFirmware["WEB01"].Seed!.Files().Select(f => f.Value)));
        Assert.Contains("group LAB-AD SRV01,DC01,WEB01", hv.Calls);
    }

    [Fact]
    public async Task An_existing_dc_is_checked_not_promoted_again()
    {
        var (labs, hv) = DcService();
        hv.Vms = [new VirtualMachine { Id = "ID-DC01", Name = "DC01", State = "Running" }];
        hv.Guest = (_, script) => script.Contains("DomainRole") ? "5" : script.Contains("Get-ADDomain") ? "corp.lab.local" : "";
        await labs.DeployAsync(LabBlueprint.Parse(DcJson));

        Assert.DoesNotContain(hv.Calls, c => c.Contains("Install-") || c.StartsWith("power", StringComparison.Ordinal));
        Assert.Contains("create SRV01", hv.Calls);

        // Nothing left that joins: the DC is not even asked
        var calls = hv.Calls.Count;
        hv.Vms.RemoveAll(v => v.Name == "WEB01");
        await labs.DeployAsync(LabBlueprint.Parse(DcJson));
        Assert.DoesNotContain(hv.Calls.Skip(calls), c => c.StartsWith("guest", StringComparison.Ordinal));
        Assert.Contains("create WEB01", hv.Calls.Skip(calls));
    }

    [Fact]
    public async Task A_dc_that_never_finishes_its_setup_stops_the_deployment()
    {
        var (labs, hv) = DcService(setup: TimeSpan.Zero);
        hv.Guest = (_, _) => throw new InvalidOperationException("The credential is invalid.");
        var error = await Assert.ThrowsAsync<TimeoutException>(() => labs.DeployAsync(LabBlueprint.Parse(DcJson)));
        Assert.Contains("The credential is invalid.", error.Message);
        Assert.DoesNotContain("create SRV01", hv.Calls);
    }

    [Fact]
    public void The_host_gateway_is_the_first_address_of_the_nat()
        => Assert.Equal("192.168.100.1", LabBlueprintService.HostGateway("192.168.100.0/24"));
}
