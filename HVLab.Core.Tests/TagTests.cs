using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

/// <summary>Free VM tags, kept as a line of the Hyper-V notes: filter, search, edit, and lab blueprints.</summary>
public class TagTests
{
    private const string Created = "Created by HV-LAB on 2026-11-16 10:00\nAccount: Administrator\nPassword: Lab#2026";

    private static VirtualMachine Vm(string name, string notes = "")
        => new() { Id = Guid.NewGuid().ToString().ToUpperInvariant(), Name = name, State = "Off", Notes = notes };

    [Fact]
    public void Tags_are_cleaned_when_parsed()
        => Assert.Equal(["web", "prod", "db"], VmNotes.ParseTags(" web, prod;; WEB ,db\n, "));

    [Fact]
    public void The_tags_line_is_replaced_and_the_other_notes_are_kept()
    {
        var notes = VmNotes.WithTags(Created, ["web", "prod"]);
        Assert.Equal(["web", "prod"], VmNotes.ReadTags(notes));
        Assert.Equal("Lab#2026", VmNotes.Read(notes).Password);              // the credentials stay readable

        var changed = VmNotes.WithTags(notes, ["db"]);
        Assert.Equal(["db"], VmNotes.ReadTags(changed));
        Assert.Single(changed.Split('\n'), l => l.Contains("db"));           // one tags line, never two

        var none = VmNotes.WithTags(changed, []);
        Assert.Empty(VmNotes.ReadTags(none));
        Assert.Equal(Created.Split('\n'), none.ReplaceLineEndings("\n").Split('\n'));
    }

    [Theory]
    [InlineData("Étiquettes : web, prod")]
    [InlineData("Tags: web, prod")]
    public void Tags_are_read_in_both_languages(string line)
        => Assert.Equal(["web", "prod"], VmNotes.ReadTags($"Some notes\r\n{line}\r\n"));

    [Fact]
    public async Task The_list_filters_and_searches_by_tag()
    {
        var web = Vm("WEB01", VmNotes.WithTags("", ["web", "prod"]));
        var db  = Vm("DB01", VmNotes.WithTags("", ["db", "prod"]));
        var vm = new VirtualMachinesViewModel(new FakeHyperVService { Vms = [web, db] }, new FakeDialogService());
        await vm.RefreshAsync();

        Assert.Equal([LocalizationService.T("VM_TagAll"), "db", "prod", "web"], vm.TagFilters);
        Assert.Equal("#web · #prod", vm.VisibleMachines.Single(v => v.Name == "WEB01").Subtitle);
        vm.SelectedTag = "web";
        Assert.Equal(["WEB01"], vm.VisibleMachines.Select(v => v.Name));
        vm.SelectedTag = vm.TagFilters[0];
        vm.SearchText = "DB";                                                // tags are searched like names
        Assert.Equal(["DB01"], vm.VisibleMachines.Select(v => v.Name));
    }

    [Fact]
    public async Task Editing_several_vms_changes_only_the_tags_they_share()
    {
        var web = Vm("WEB01", VmNotes.WithTags(Created, ["web", "prod"]));
        var db  = Vm("DB01", VmNotes.WithTags("", ["db", "prod"]));
        var hv = new FakeHyperVService { Vms = [web, db] };
        var dialogs = new FakeDialogService { PromptAnswer = "lab, test" };   // "prod" removed, "lab" and "test" added
        var vm = new VirtualMachinesViewModel(hv, dialogs);
        await vm.RefreshAsync();

        await vm.EditTagsAsync(vm.VirtualMachines.ToList());
        Assert.Equal("prod", dialogs.LastPromptInitial);                     // the shared tags are offered
        Assert.Contains($"notes {web.Id} web,lab,test", hv.Calls);
        Assert.Contains($"notes {db.Id} db,lab,test", hv.Calls);
        Assert.Equal("Lab#2026", VmNotes.Read(hv.Vms[0].Notes).Password);

        dialogs.PromptAnswer = "";                                           // empty: no tag left
        await vm.EditTagsAsync([vm.VirtualMachines.Single(v => v.Name == "DB01")]);
        Assert.Contains($"notes {db.Id} -", hv.Calls);
    }

    [Fact]
    public async Task Blueprint_tags_go_into_the_notes_and_come_back_on_export()
    {
        var bases = Directory.CreateTempSubdirectory("hvlab-tags-").FullName;
        File.WriteAllBytes(Path.Combine(bases, "BASE_WindowsServer2025Standard_10.0.26100.1_50.vhdx"), [1]);
        var hv = new FakeHyperVService();
        var labs = new LabBlueprintService(hv, new FakeNatService(), new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            BaseFolder = () => bases, VmsFolder = () => bases,
        };
        var lab = LabBlueprint.Parse("""
            { "name": "LAB", "switch": "LAN", "password": "Lab#2026",
              "vms": [ { "name": "WEB01", "base": "WindowsServer2025*", "tags": ["web", "prod"] } ] }
            """);
        await labs.DeployAsync(lab);
        Assert.Equal(["web", "prod"], VmNotes.ReadTags(hv.CreatedNotes["WEB01"]));

        var created = new VirtualMachine { Name = "WEB01", SwitchName = "LAN", Notes = hv.CreatedNotes["WEB01"]! };
        var exported = await labs.ExportAsync("LAB", [created]);
        Assert.Equal(["web", "prod"], exported.Vms.Single().Tags);

        // In the Labs editor: typed as text, saved as a clean list
        var row = LabVmRow.From(exported.Vms.Single());
        Assert.Equal("web, prod", row.Tags);
        row.Tags = "web, , qa";
        Assert.Equal(["web", "qa"], row.ToVm().Tags);
        row.Tags = " ";
        Assert.Null(row.ToVm().Tags);
    }
}
