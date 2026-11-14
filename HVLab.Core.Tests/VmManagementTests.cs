using HVLab.Models;
using HVLab.Services;
using HVLab.Services.Wmi;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class WmiMapTests
{
    [Theory]
    [InlineData(2, "Running")]
    [InlineData(3, "Off")]
    [InlineData(6, "Saved")]
    [InlineData(32769, "Saved")]
    [InlineData(9, "Paused")]
    [InlineData(32768, "Paused")]
    [InlineData(10, "Starting")]
    [InlineData(4, "Stopping")]
    [InlineData(99, "Other")]
    public void Enabled_states_map_to_get_vm_names(long state, string name) => Assert.Equal(name, WmiMap.StateName(state));

    [Fact]
    public void Generation_and_uptime()
    {
        Assert.Equal(1, WmiMap.Generation("Microsoft:Hyper-V:SubType:1"));
        Assert.Equal(2, WmiMap.Generation("Microsoft:Hyper-V:SubType:2"));
        Assert.Equal(2, WmiMap.Generation(null));
        Assert.Equal("01:02:03", WmiMap.FormatUptime(3_723_000));
        Assert.Equal("1.00:00:05", WmiMap.FormatUptime(86_405_000));
    }

    [Fact]
    public void Heartbeat_codes()
    {
        Assert.Equal("OK", WmiMap.Heartbeat(2));
        Assert.Equal("Lost", WmiMap.Heartbeat(12));
        Assert.Equal("", WmiMap.Heartbeat(null));
    }

    [Theory]
    [InlineData(@"Microsoft:1fb0d232-a460-4a47-80b1-d5e9110fc7f8\4764334E-E001-4176-82EE-5594EC9B530E", "1FB0D232-A460-4A47-80B1-D5E9110FC7F8")]
    [InlineData(@"Microsoft:GuestNetwork\5D41A37D-6E40-4494-AF77-BEC50861610E\D7324A64-CF03-4081-9BAE-653F7B01B36E", "5D41A37D-6E40-4494-AF77-BEC50861610E")]
    [InlineData(@"Microsoft:Definition\B637F347-6A0E-4DEC-AF52-BD70CB80A21D\Default", null)]
    [InlineData("", null)]
    public void Owner_of_a_resource_instance_id(string instanceId, string? owner) => Assert.Equal(owner, WmiMap.OwnerId(instanceId));

    [Fact]
    public void Keys_are_read_from_object_paths()
    {
        const string path = @"\\HOST\root\virtualization\v2:Msvm_VirtualEthernetSwitch.CreationClassName=""Msvm_VirtualEthernetSwitch"",Name=""71A45029-4C16-4B67-B19E-765B11C652DD""";
        Assert.Equal("71A45029-4C16-4B67-B19E-765B11C652DD", WmiMap.PathKey(path, "Name"));
        Assert.Null(WmiMap.PathKey(path, "DeviceID"));
    }

    [Fact]
    public void Mac_and_ip_helpers()
    {
        Assert.Equal("00155D010203", WmiMap.NormalizeMac("00-15-5d-01-02-03"));
        Assert.Equal("192.168.100.10", WmiMap.FirstIpv4(["fe80::1", "169.254.3.4", "192.168.100.10", "10.0.0.1"]));
        Assert.Null(WmiMap.FirstIpv4(["fe80::1"]));
    }

    [Fact]
    public void Vlan_text_and_switch_type()
    {
        Assert.Equal("10", WmiMap.VlanText(1, 10, null));
        Assert.Equal("", WmiMap.VlanText(1, 0, null));
        Assert.Equal("10,20", WmiMap.VlanText(2, 0, [10, 20]));
        Assert.Equal("External", WmiMap.SwitchType(true, true));
        Assert.Equal("Internal", WmiMap.SwitchType(false, true));
        Assert.Equal("Private", WmiMap.SwitchType(false, false));
    }

    [Fact]
    public void Nat_networks_find_their_internal_switch()
    {
        (string, string)[] addresses = [("Ethernet", "192.168.1.20"), ("vEthernet (NAT-LAB)", "192.168.100.1"), ("vEthernet (CNED)", "10.0.0.1")];
        Assert.Equal("NAT-LAB", WmiMap.NatSwitch("192.168.100.0/24", addresses));
        Assert.Equal("CNED", WmiMap.NatSwitch("10.0.0.0/24", addresses));
        Assert.Equal("", WmiMap.NatSwitch("172.16.0.0/16", addresses));
        Assert.Equal("", WmiMap.NatSwitch("bad", addresses));
    }

    [Fact]
    public void Checkpoint_tree_orders_children_and_marks_now()
    {
        var t0 = new DateTime(2026, 11, 1);
        VmCheckpoint[] cps =
        [
            new("B", "Roles installed", "A", t0.AddHours(2)),
            new("A", "Fresh install", null, t0),
            new("C", "Before update", "A", t0.AddHours(1)),
            new("D", "Patched", "B", t0.AddHours(3)),
        ];
        var rows = WmiMap.CheckpointTree(cps, "B");
        Assert.Equal(["Fresh install", "Before update", "Roles installed", "Patched", ""], rows.Select(r => r.Name));
        Assert.Equal([0, 1, 1, 2, 2], rows.Select(r => r.Depth));
        Assert.True(rows[^1].IsNow);
        Assert.Empty(WmiMap.CheckpointTree([], null));
    }

    [Fact]
    public void Rgb565_pixels_become_bgra()
    {
        // white (0xFFFF) then pure red (0xF800), little endian
        var bgra = WmiMap.Rgb565ToBgra([0xFF, 0xFF, 0x00, 0xF8], 2, 1);
        Assert.Equal([255, 255, 255, 255, 0, 0, 255, 255], bgra);
    }

    [Fact]
    public void Thumbnail_keeps_the_guest_aspect_ratio()
    {
        Assert.Equal((320, 180), WmiMap.FitThumbnail(1920, 1080, 320, 240));
        Assert.Equal((320, 240), WmiMap.FitThumbnail(1024, 768, 320, 240));
        Assert.Equal((320, 240), WmiMap.FitThumbnail(0, 0, 320, 240));
        // never upscaled: the console asks for up to 2560×1600 and gets the native 1024×768
        Assert.Equal((1024, 768), WmiMap.FitThumbnail(1024, 768, 2560, 1600));
    }
}

public class VmNotesReadTests
{
    [Theory]
    [InlineData("Créée par HV-LAB le 02/11/2026 10:50\r\nCompte : Administrator\r\nMot de passe : 123+aze\r\nIP : 192.168.100.10")]
    [InlineData("Created by HV-LAB on 2026-11-02 10:50\nAccount: Administrator\nPassword: 123+aze\nIP: 192.168.100.10")]
    public void Credentials_are_read_back_in_both_languages(string notes)
    {
        var c = VmNotes.Read(notes);
        Assert.Equal("Administrator", c.Account);
        Assert.Equal("123+aze", c.Password);
        Assert.Equal("192.168.100.10", c.IpAddress);
    }

    [Fact]
    public void Foreign_notes_give_empty_credentials()
    {
        var c = VmNotes.Read("2Pint Lab Kit version 26.09.27");
        Assert.False(c.HasPassword);
        Assert.Equal("", c.Account);
        // the detail pane then hides the account line and the copy/type buttons
        Assert.False(new VirtualMachine { Notes = "2Pint Lab Kit version 26.09.27" }.HasCredentials);
        Assert.True(new VirtualMachine { Notes = "Account: Administrator\nPassword: x" }.HasCredentials);
    }
}

public class KeyboardScancodesTests
{
    [Fact]
    public void Us_layout_types_qwerty_keys()
        => Assert.Equal([0x1E, 0x9E], KeyboardScancodes.Build("a", KeyboardScancodes.Klid("en-US"), false));

    [Fact]
    public void French_layout_uses_the_azerty_positions()
    {
        var fr = KeyboardScancodes.Klid("fr-FR");
        Assert.Equal([0x10, 0x90], KeyboardScancodes.Build("a", fr, false));                       // A is where Q is on QWERTY
        Assert.Equal([0x2A, 0x02, 0x82, 0xAA], KeyboardScancodes.Build("1", fr, false));           // digits need Shift
        Assert.Equal([0xE0, 0x38, 0x0B, 0x8B, 0xE0, 0xB8], KeyboardScancodes.Build("@", fr, false)); // AltGr + 0
        Assert.Equal([0x1C, 0x9C], KeyboardScancodes.Build("", fr, true));                          // Enter
    }
}

public class VmSettingsTests
{
    [Fact]
    public void Validation_rules()
    {
        Assert.Null(new VmSettings().Validate());
        Assert.Equal("Val_VmCpu", new VmSettings { ProcessorCount = 0 }.Validate());
        Assert.Equal("Val_VmMemory", new VmSettings { StartupMB = 1025 }.Validate());
        Assert.Equal("Val_VmMemoryRange", new VmSettings { DynamicMemory = true, MinimumMB = 4096, StartupMB = 2048, MaximumMB = 8192 }.Validate());
        Assert.Equal("Val_VmIso", new VmSettings { IsoPath = @"C:\x.img" }.Validate());
        var nested = new VmSettings { DynamicMemory = true };
        nested.Nested = true;
        Assert.False(nested.DynamicMemory);   // nested virtualization switches to static memory
    }

    [Fact]
    public void Only_changed_values_are_applied_and_cpu_needs_the_vm_off()
    {
        var before = new VmSettings { ProcessorCount = 2, StartupMB = 2048, IsRunning = true, IsoPath = @"C:\a.iso", VlanId = 10 };
        var after = before.Clone();
        after.ProcessorCount = 4;      // ignored: the VM runs
        after.IsoPath = "";
        after.VlanId = 0;
        var lines = HyperVService.SettingsScript(before, after);
        Assert.Equal(2, lines.Count);
        Assert.Contains("Set-VMDvdDrive -Path $null", lines[0]);
        Assert.Contains("-Untagged", lines[1]);

        before.IsRunning = false;
        var off = before.Clone();
        off.ProcessorCount = 4;
        off.Nested = true;
        var offLines = HyperVService.SettingsScript(before, off);
        Assert.Contains(offLines, l => l.Contains("-Count 4 -ExposeVirtualizationExtensions $true"));
        Assert.Contains(offLines, l => l.Contains("-MacAddressSpoofing On"));
        Assert.Empty(HyperVService.SettingsScript(before, before.Clone()));
    }
}

public class VmManagementViewModelTests
{
    private static VirtualMachine Vm(string name, string state, string group = "", string notes = "")
        => new() { Id = Guid.NewGuid().ToString().ToUpperInvariant(), Name = name, State = state, GroupName = group, Notes = notes };

    [Fact]
    public async Task Power_actions_use_the_vm_id()
    {
        var a = Vm("A", "Running");
        var hv = new FakeHyperVService { Vms = [a] };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        var row = vm.VirtualMachines[0];
        await vm.TurnOffVmCommand.ExecuteAsync(row);
        await vm.SaveVmCommand.ExecuteAsync(row);
        Assert.Equal([$"power {a.Id} TurnOff", $"power {a.Id} Save"], hv.Calls);
    }

    [Fact]
    public async Task An_action_error_survives_the_refresh_until_closed_or_the_next_action()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Running")], PowerError = new InvalidOperationException("boom") };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        var row = vm.VirtualMachines[0];

        await vm.TurnOffVmCommand.ExecuteAsync(row);      // fails, then refreshes silently
        Assert.Equal("boom", vm.ErrorMessage);
        await vm.RefreshAsync(silent: true);              // a poll does not hide it
        Assert.True(vm.HasError);

        vm.DismissErrorCommand.Execute(null);             // close button of the banner
        Assert.False(vm.HasError);

        await vm.TurnOffVmCommand.ExecuteAsync(row);
        hv.PowerError = null;
        await vm.SaveVmCommand.ExecuteAsync(row);         // the next action starts clean
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task A_load_error_clears_on_the_next_successful_refresh()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off")], Failure = new InvalidOperationException("wmi down") };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        Assert.Equal("wmi down", vm.ErrorMessage);
        hv.Failure = null;
        await vm.RefreshAsync(silent: true);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task The_vm_window_shows_an_action_error_until_the_next_action()
    {
        var a = Vm("A", "Running");
        var hv = new FakeHyperVService { Vms = [a], PowerError = new InvalidOperationException("boom") };
        var d = new VmDetailsViewModel(hv, new FakeDialogService());
        d.Initialize(a);
        await d.TurnOffCommand.ExecuteAsync(null);
        Assert.Equal("boom", d.ErrorMessage);
        await d.RefreshAsync();
        Assert.True(d.HasError);
        hv.PowerError = null;
        await d.SaveCommand.ExecuteAsync(null);
        Assert.False(d.HasError);
    }

    [Theory]
    [InlineData(true, "remove A 1 files")]
    [InlineData(false, "remove A 0 files")]
    public async Task Delete_removes_the_disks_only_when_asked(bool withDisks, string expected)
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off")] };
        var dialogs = new FakeDialogService { Option = withDisks };
        var vm = new VirtualMachinesViewModel(hv, dialogs);
        await vm.RefreshAsync();
        await vm.RemoveVmCommand.ExecuteAsync(vm.VirtualMachines[0]);
        Assert.Equal(expected, Assert.Single(hv.Calls));
        Assert.Contains("X.vhdx", dialogs.LastContent);          // the files are listed
        Assert.Contains("BASE_X.vhdx", dialogs.LastContent);     // and the kept base image too
        Assert.NotNull(dialogs.LastOptionLabel);
    }

    [Fact]
    public async Task Bulk_start_skips_running_vms_and_group_filter_narrows_the_list()
    {
        var a = Vm("SRV-01", "Off", "LAB"); var b = Vm("SRV-02", "Running", "LAB"); var c = Vm("CLI-01", "Off");
        var hv = new FakeHyperVService { Vms = [a, b, c] };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Groups.Count);                       // "all groups" + LAB
        Assert.Equal("LAB", vm.Groups[1]);

        vm.SetSelection(vm.VirtualMachines);
        Assert.True(vm.HasMultiSelection);
        await vm.StartSelectedCommand.ExecuteAsync(null);
        Assert.Equal(new[] { $"power {a.Id} Start", $"power {c.Id} Start" }.Order(), hv.Calls.Order());

        vm.SelectedGroup = "LAB";
        Assert.Equal(["SRV-01", "SRV-02"], vm.VisibleMachines.Select(v => v.Name));
        vm.SelectedGroup = vm.Groups[0];
        Assert.Equal(3, vm.VisibleMachines.Count);
    }

    [Fact]
    public async Task Grouping_suggests_the_bulk_prefix()
    {
        var hv = new FakeHyperVService { Vms = [Vm("SRV-01", "Off"), Vm("SRV-02", "Off")] };
        var dialogs = new FakeDialogService { PromptAnswer = "LAB-AD" };
        var vm = new VirtualMachinesViewModel(hv, dialogs);
        await vm.RefreshAsync();
        Assert.Equal("SRV", VirtualMachinesViewModel.CommonPrefix(vm.VirtualMachines.ToList()));

        vm.SetSelection(vm.VirtualMachines);
        await vm.GroupSelectedCommand.ExecuteAsync(null);
        Assert.Contains("group LAB-AD SRV-01,SRV-02", hv.Calls);
        Assert.All(vm.VirtualMachines, v => Assert.Equal("LAB-AD", v.GroupName));
    }

    private static VmDetailsViewModel Details(FakeHyperVService hv, FakeDialogService dialogs, VirtualMachine v)
    {
        var d = new VmDetailsViewModel(hv, dialogs);
        d.Initialize(v);
        return d;
    }

    [Fact]
    public async Task Type_password_uses_the_notes_and_the_chosen_layout()
    {
        var a = Vm("DC01", "Running", notes: "Created by HV-LAB on 2026-11-02 10:50\nAccount: Administrator\nPassword: P@ss");
        var hv = new FakeHyperVService { Vms = [a] };
        var d = Details(hv, new FakeDialogService(), a);
        d.TypingLanguage = "fr-FR";
        await d.TypePasswordCommand.ExecuteAsync(null);
        Assert.Contains($"type {a.Id} P@ss fr-FR True", hv.Calls);
    }

    [Fact]
    public async Task Window_refresh_follows_the_vm_and_reports_its_removal()
    {
        var a = Vm("A", "Off");
        var hv = new FakeHyperVService { Vms = [a] };
        var d = Details(hv, new FakeDialogService(), a);
        var removed = false;
        d.VmRemoved += (_, _) => removed = true;
        a.State = "Running";
        await d.RefreshAsync();
        Assert.Equal("Running", d.Vm.State);
        hv.Vms.Clear();
        await d.RefreshAsync();
        Assert.True(removed);
    }

    [Fact]
    public async Task Checkpoint_actions_address_the_selected_checkpoint()
    {
        var a = Vm("A", "Off");
        var hv = new FakeHyperVService
        {
            Vms = [a],
            CheckpointRows = [new CheckpointRow("CP1", "Fresh", null, 0, false), new CheckpointRow("", "", null, 1, true)],
        };
        var d = Details(hv, new FakeDialogService { PromptAnswer = "Renamed" }, a);
        await d.LoadCheckpointsAsync();
        Assert.Equal(2, d.Checkpoints.Count);

        d.SelectedCheckpoint = d.Checkpoints[1];             // "Now" is not a checkpoint
        Assert.False(d.HasCheckpointSelection);
        d.SelectedCheckpoint = d.Checkpoints[0];
        await d.ApplyCheckpointCommand.ExecuteAsync(null);
        await d.RenameCheckpointCommand.ExecuteAsync(null);
        await d.RemoveCheckpointCommand.ExecuteAsync(null);
        Assert.Equal(["apply A CP1", "rename-checkpoint A CP1 Renamed", "remove-checkpoint A CP1 False"], hv.Calls);
    }

    [Fact]
    public async Task Settings_are_validated_then_applied()
    {
        var a = Vm("A", "Off");
        var hv = new FakeHyperVService { Vms = [a], Settings = new VmSettings { ProcessorCount = 2 } };
        var d = Details(hv, new FakeDialogService(), a);
        await d.LoadSettingsAsync();
        d.EditingSettings!.ProcessorCount = 0;
        await d.ApplySettingsCommand.ExecuteAsync(null);
        Assert.NotEmpty(d.SettingsError);
        Assert.DoesNotContain(hv.Calls, c => c.StartsWith("settings"));
        d.EditingSettings.ProcessorCount = 4;
        await d.ApplySettingsCommand.ExecuteAsync(null);
        Assert.Empty(d.SettingsError);
        Assert.Contains(hv.Calls, c => c.StartsWith("settings A") && c.Contains("-Count 4"));
    }

    [Fact]
    public async Task Window_power_actions_use_the_vm_id()
    {
        var a = Vm("A", "Running");
        var hv = new FakeHyperVService { Vms = [a] };
        var d = Details(hv, new FakeDialogService(), a);
        await d.ShutdownCommand.ExecuteAsync(null);
        await d.TurnOffCommand.ExecuteAsync(null);
        Assert.Equal([$"power {a.Id} Shutdown", $"power {a.Id} TurnOff"], hv.Calls);
    }
    [Fact]
    public async Task A_bulk_series_becomes_a_hyperv_group()
    {
        var dir = Directory.CreateTempSubdirectory("hvlab-group-").FullName;
        var basePath = Path.Combine(dir, "BASE_WindowsServer2022Standard_10.0.20348.587_50.vhdx");
        File.WriteAllBytes(basePath, []);
        var hv = new FakeHyperVService();
        var vm = new CreateVmViewModel(hv, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            SelectedBaseVhdx = basePath, SelectedSwitch = "LAN", AdminPassword = "Lab#2026",
            BulkMode = true, BulkPrefix = "SRV", BulkCount = 2,
        };
        await vm.CreateVmAsync();
        Assert.Contains("group SRV SRV-01,SRV-02", hv.Calls);
    }
}

public class VmScriptTests
{
    [Fact]
    public async Task Delete_plan_protects_the_base_images_folder()
    {
        var runner = new FakePowerShellRunner(_ => """{"VmFolder":"C:\\VMs\\A","Files":"C:\\VMs\\A\\A.vhdx","KeptParents":[]}""");
        var plan = await new HyperVService(runner).GetDeletePlanAsync("A");
        Assert.Equal([@"C:\VMs\A\A.vhdx"], plan.Files);      // a one-item array unrolled by PowerShell still reads
        Assert.Contains("$baseDir", runner.Scripts.Single());
    }

    [Fact]
    public async Task Remove_with_files_deletes_exactly_the_listed_files()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).RemoveVMAsync("A", [@"C:\VMs\A\Virtual Hard Disks\A.vhdx", @"C:\VMs\A\Virtual Hard Disks\A-cidata.vhdx"]);
        var script = runner.Scripts.Single();
        Assert.Contains(@"'C:\VMs\A\Virtual Hard Disks\A-cidata.vhdx'", script);
        Assert.Contains("Remove-VM", script);
    }

    [Fact]
    public async Task Checkpoint_ids_must_be_guids()
        => await Assert.ThrowsAsync<FormatException>(() =>
            new HyperVService(new FakePowerShellRunner(_ => "")).ApplyCheckpointAsync("A", "x'; Remove-VM *"));
}
