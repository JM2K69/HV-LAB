using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

/// <summary>The graphical lab editor: pick bases, sizes and network in the form, and it writes the JSON.</summary>
public class LabsViewModelTests
{
    private static (LabsViewModel Vm, FakeHyperVService Hv, FakeDialogService Dialogs, string Folder) Editor()
    {
        var folder = Directory.CreateTempSubdirectory("hvlab-labs-").FullName;
        foreach (var name in new[] { "BASE_WindowsServer2025Standard_10.0.26100.4652_50", "BASE_LinuxAlma_9.6.0.0_40" })
            File.WriteAllBytes(Path.Combine(folder, name + ".vhdx"), [1]);
        var hv = new FakeHyperVService { Switches = [new VirtualSwitch { Name = "LAN", SwitchType = "Internal" }] };
        var labs = new LabBlueprintService(hv, new FakeNatService(), new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            BaseFolder = () => folder, VmsFolder = () => folder,
        };
        var dialogs = new FakeDialogService();
        return (new LabsViewModel(labs, hv, new VhdxService(new FakePowerShellRunner(_ => "[]")), dialogs), hv, dialogs, folder);
    }

    [Fact]
    public async Task The_form_offers_the_host_bases_and_switches()
    {
        var (vm, _, _, _) = Editor();
        await vm.LoadAsync();
        Assert.Equal(["LinuxAlma*", "WindowsServer2025Standard*"], vm.BasePatterns);
        Assert.Equal(["LAN"], vm.SwitchNames);
        Assert.True(vm.HasNoEditor);
    }

    [Fact]
    public async Task Filling_the_form_generates_and_saves_the_json()
    {
        var (vm, _, _, folder) = Editor();
        await vm.LoadAsync();
        await vm.NewLabCommand.ExecuteAsync(null);
        Assert.True(vm.HasEditor);
        Assert.Equal(3, vm.Vms.Count);                                   // the template: DC, member, Linux

        vm.LabName = "LAB-WEB";
        vm.SwitchName = "LAN";
        vm.UseNat = false;
        vm.UseDomain = false;
        vm.Password = "Lab#2026";
        while (vm.Vms.Count > 0) vm.RemoveVmCommand.Execute(vm.Vms[0]);
        vm.AddVmCommand.Execute(null);
        var row = vm.Vms.Single();
        row.Name = "WEB01"; row.Base = "LinuxAlma*"; row.MemoryMB = 4096; row.Cpu = 4; row.User = "web";

        Assert.Contains("\"name\": \"WEB01\"", vm.JsonPreview);          // the JSON follows the form
        Assert.DoesNotContain("Lab#2026", vm.JsonPreview);               // password masked in the preview
        Assert.True(vm.IsDirty);

        Assert.True(await vm.SaveAsync());
        Assert.False(vm.IsDirty);
        var saved = LabBlueprint.Parse(File.ReadAllText(Path.Combine(folder, "Blueprints", "LAB-WEB.json")));
        var web = saved.Vms.Single();
        Assert.Equal(("LinuxAlma*", 4096L, 4, "web"), (web.Base, web.MemoryMB, web.Cpu, web.User));
        Assert.Null(saved.Nat);
        Assert.Single(vm.Files);
    }

    [Fact]
    public async Task Rows_keep_the_creation_order_and_drop_what_does_not_apply()
    {
        var (vm, _, _, _) = Editor();
        await vm.LoadAsync();
        await vm.NewLabCommand.ExecuteAsync(null);
        var linux = vm.Vms[2];
        vm.MoveUpCommand.Execute(linux);
        Assert.Same(linux, vm.Vms[1]);
        linux.Join = true;                                               // Linux: never joins the domain
        Assert.False(vm.ToBlueprint().Vms[1].Join);
        Assert.Null(vm.ToBlueprint().Vms[0].User);                       // Windows: no Linux user
    }

    [Fact]
    public async Task An_invalid_form_is_not_saved_and_says_why()
    {
        var (vm, _, _, _) = Editor();
        await vm.LoadAsync();
        await vm.NewLabCommand.ExecuteAsync(null);
        vm.Vms[0].Ip = "192.168.100.999";
        Assert.False(await vm.SaveAsync());
        Assert.Contains("DC01", vm.ErrorMessage);
    }

    [Fact]
    public async Task The_plan_shows_what_would_be_created()
    {
        var (vm, _, _, _) = Editor();
        await vm.LoadAsync();
        await vm.NewLabCommand.ExecuteAsync(null);
        vm.Password = "Lab#2026";
        await vm.CheckPlanCommand.ExecuteAsync(null);
        Assert.True(vm.HasPlan);
        Assert.Equal(3, vm.Plan.Count(p => p.Action == LabBlueprintService.Create && p.Kind == LocalizationService.T("LAB_Kind_vm")));
        vm.LabName = "OTHER";                                            // an edit makes the plan stale
        Assert.False(vm.HasPlan);
    }

    [Fact]
    public async Task Deploying_saves_then_creates_the_vms_after_confirmation()
    {
        var (vm, hv, _, _) = Editor();
        await vm.LoadAsync();
        await vm.NewLabCommand.ExecuteAsync(null);
        vm.Password = "Lab#2026";
        await vm.DeployCommand.ExecuteAsync(null);
        Assert.Equal(["DC01", "SRV01", "WEB01"], hv.CreatedNotes.Keys.Order());
        Assert.Contains(hv.Calls, c => c.StartsWith("group LAB-AD"));
        Assert.True(vm.HasSuccess);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task An_exported_blueprint_opens_in_the_editor()
    {
        var (vm, _, _, folder) = Editor();
        var blueprints = Path.Combine(folder, "Blueprints");
        Directory.CreateDirectory(blueprints);
        var file = Path.Combine(blueprints, "EXPORTED.json");
        File.WriteAllText(file, new LabBlueprint { Name = "EXPORTED", Switch = "LAN", Vms = [new LabVm { Name = "A", Base = "LinuxAlma*" }] }.ToJson());
        var labs = (LabBlueprintService)typeof(LabsViewModel).GetField("<labs>P", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                       .GetValue(vm)!;
        labs.RequestedFile = file;
        await vm.LoadAsync();
        Assert.True(vm.HasEditor);
        Assert.Equal("EXPORTED", vm.LabName);
        Assert.Null(labs.RequestedFile);                                 // opened once
    }
}
