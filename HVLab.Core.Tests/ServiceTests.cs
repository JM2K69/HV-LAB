using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class HyperVParsingTests
{
    private const string TwoVms = """
        [{"Name":"SRV-01","State":"Running","ProcessorCount":2,"MemoryMB":4096,"Generation":2,"SwitchName":"LAN","Uptime":"01:02:03","VlanInfo":"10"},
         {"Name":"SRV-02","State":"Off","ProcessorCount":1,"MemoryMB":2048,"Generation":1,"SwitchName":null,"Uptime":"00:00:00","VlanInfo":12}]
        """;

    [Fact]
    public async Task Vms_are_parsed_from_json()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => TwoVms));
        var vms = await hv.GetVirtualMachinesAsync();

        Assert.Equal(2, vms.Count);
        Assert.True(vms[0].IsRunning);
        Assert.Equal(4096, vms[0].MemoryMB);
        Assert.Equal("", vms[1].SwitchName);     // null → empty
        Assert.Equal("12", vms[1].VlanInfo);      // number → text (no exception)
    }

    [Fact]
    public async Task Stray_output_before_the_json_is_ignored()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "WARNING: something\r\n" + TwoVms));
        Assert.Equal(2, (await hv.GetVirtualMachinesAsync()).Count);
    }

    [Fact]
    public async Task Unreadable_output_is_an_error_not_an_empty_list()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "[not json"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(hv.GetVirtualMachinesAsync);
        Assert.Contains("[not json", ex.Message);
    }

    [Fact]
    public async Task Empty_array_gives_no_vm()
    {
        var hv = new HyperVService(new FakePowerShellRunner(_ => "[]"));
        Assert.Empty(await hv.GetVirtualMachinesAsync());
    }

    [Fact]
    public async Task Vm_names_are_escaped_in_scripts()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).StartVMAsync("it's");
        Assert.Contains("'it''s'", runner.Scripts.Single());
    }

    [Fact]
    public async Task Invalid_vm_name_never_reaches_powershell()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new HyperVService(runner).CreateBlankVmAsync(@"..\x", "LAN", 2048, 2, 2, @"C:\VMs", 40));
        Assert.Empty(runner.Scripts);
    }

    [Fact]
    public async Task Answer_file_is_embedded_not_written_to_temp()
    {
        var runner = new FakePowerShellRunner(_ => "");
        await new HyperVService(runner).CreateVMWithDifferencingDiskAsync(
            "SRV-01", @"C:\Base\BASE.vhdx", "LAN", 2048, 2, 2, @"C:\VMs", answerFileContent: "<unattend/>");
        var script = runner.Scripts.Single();
        Assert.Contains("FromBase64String", script);
        Assert.DoesNotContain("<unattend/>", script);
        Assert.DoesNotContain("hvlab_unattend_", script);
    }

    [Fact]
    public async Task Notes_are_set_on_the_vm_and_embedded_like_the_answer_file()
    {
        var runner = new FakePowerShellRunner(_ => "");
        var notes  = "Account: Administrator\r\nPassword: P@ss'w0rd";
        await new HyperVService(runner).CreateBlankVmAsync("SRV-01", "LAN", 2048, 2, 2, @"C:\VMs", 40, notes: notes);
        var script = runner.Scripts.Single();
        Assert.Contains("-Notes $notes", script);
        Assert.Contains(HyperVService.PsLiteral(notes), script);
        Assert.DoesNotContain("P@ss'w0rd", script);
    }
}

public class VmNotesTests
{
    [Fact]
    public void Unattended_vm_notes_hold_the_date_the_account_and_its_password()
    {
        var lines = VmNotes.Build(new DateTime(2026, 11, 2, 14, 35, 0), "Administrator", "P@ssw0rd").Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.Contains("2026", lines[0]);
        Assert.EndsWith("Administrator", lines[1]);
        Assert.EndsWith("P@ssw0rd", lines[2]);
    }

    [Fact]
    public void Vm_without_answer_file_has_no_password_in_its_notes()
    {
        var lines = VmNotes.Build(new DateTime(2026, 11, 2, 14, 35, 0)).Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("2026", lines[0]);
    }

    private static string TempBaseImage()
    {
        var dir = Directory.CreateTempSubdirectory("hvlab-notes-").FullName;
        var path = Path.Combine(dir, "BASE_WindowsServer2022Standard_10.0.20348.587_50.vhdx");
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public async Task Every_vm_of_a_series_gets_the_administrator_password_in_its_notes()
    {
        var hv = new FakeHyperVService();
        var vm = new CreateVmViewModel(hv, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            SelectedBaseVhdx = TempBaseImage(), SelectedSwitch = "LAN",
            AdminPassword = "Lab#2026", BulkMode = true, BulkPrefix = "SRV", BulkCount = 2,
        };
        await vm.CreateVmAsync();

        Assert.Equal(["SRV-01", "SRV-02"], hv.CreatedNotes.Keys.Order());
        Assert.All(hv.CreatedNotes.Values, n =>
        {
            Assert.Contains(AnswerFileGenerator.AdministratorAccount, n);
            Assert.Contains("Lab#2026", n);
        });
    }

    [Fact]
    public async Task Quick_vm_notes_hold_no_account()
    {
        var hv = new FakeHyperVService();
        var vm = new QuickVmViewModel(hv, new VhdxService(new FakePowerShellRunner(_ => "[]")))
        {
            SelectedSwitch = "LAN", VmName = "PXE-01",
        };
        vm.SizeItems[0].SelectCommand.Execute(null);
        await vm.CreateVmAsync();

        var notes = Assert.Single(hv.CreatedNotes).Value;
        Assert.False(string.IsNullOrEmpty(notes));
        Assert.DoesNotContain(AnswerFileGenerator.AdministratorAccount, notes);
    }
}

public class VirtualMachinesViewModelTests
{
    private static VirtualMachine Vm(string name, string state) => new() { Name = name, State = state };

    [Fact]
    public async Task Refresh_updates_rows_in_place()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off"), Vm("B", "Running")] };
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        await vm.RefreshAsync();
        var rowA = vm.VirtualMachines.Single(v => v.Name == "A");

        hv.Vms = [Vm("A", "Running"), Vm("C", "Off")];
        await vm.RefreshAsync();

        Assert.Same(rowA, vm.VirtualMachines.Single(v => v.Name == "A")); // same instance, updated
        Assert.Equal("Running", rowA.State);
        Assert.Equal(["A", "C"], vm.VirtualMachines.Select(v => v.Name).Order());
    }

    [Fact]
    public async Task Empty_state_only_after_a_successful_refresh()
    {
        var hv = new FakeHyperVService();
        var vm = new VirtualMachinesViewModel(hv, new FakeDialogService());
        Assert.False(vm.IsEmpty);
        await vm.RefreshAsync();
        Assert.True(vm.IsEmpty);

        hv.Failure = new InvalidOperationException("boom");
        await vm.RefreshAsync();
        Assert.True(vm.HasError);
        Assert.False(vm.IsEmpty);
        Assert.Equal("boom", vm.ErrorMessage);
    }

    [Fact]
    public async Task Remove_asks_for_confirmation_and_stops_when_declined()
    {
        var hv = new FakeHyperVService { Vms = [Vm("A", "Off")] };
        var dialogs = new FakeDialogService(answer: false);
        var vm = new VirtualMachinesViewModel(hv, dialogs);
        await vm.RefreshAsync();

        await vm.RemoveVmCommand.ExecuteAsync(vm.VirtualMachines[0]);
        Assert.Equal(1, dialogs.Calls);
        Assert.False(vm.VirtualMachines[0].IsBusy);
    }

    [Fact]
    public void Action_availability_follows_state_and_busy_flag()
    {
        var v = Vm("A", "Off");
        Assert.True(v.CanStart);  Assert.False(v.CanStop); Assert.True(v.CanRemove);
        v.State = "Running";
        Assert.False(v.CanStart); Assert.True(v.CanStop);  Assert.False(v.CanRemove);
        v.IsBusy = true;
        Assert.False(v.CanStop);
    }
}

public class PowerShellRunnerTests
{
    [Fact]
    public async Task Sequential_calls_reuse_the_same_warm_process()
    {
        using var runner = new PowerShellRunner();
        var pid1 = (await runner.RunAsync("$PID")).Trim();
        var pid2 = (await runner.RunAsync("$PID")).Trim();
        Assert.Equal(pid1, pid2);
    }

    [Fact]
    public async Task Variables_do_not_leak_between_scripts()
    {
        using var runner = new PowerShellRunner();
        await runner.RunAsync("$leak = 'x'");
        Assert.Equal("", (await runner.RunAsync("\"$leak\"")).Trim());
    }

    [Fact]
    public async Task Exit_in_a_script_is_reported_and_the_worker_is_replaced()
    {
        using var runner = new PowerShellRunner();
        var pid = (await runner.RunAsync("$PID")).Trim();
        var ex  = await Assert.ThrowsAsync<PowerShellException>(() => runner.RunAsync("Write-Output 'a'; exit 3"));
        Assert.Equal(3, ex.ExitCode);
        Assert.NotEqual(pid, (await runner.RunAsync("$PID")).Trim());
    }

    [Fact]
    public async Task Non_terminating_errors_do_not_fail_the_script()
    {
        using var runner = new PowerShellRunner();
        Assert.Equal("after", (await runner.RunAsync("Write-Error 'soft'; 'after'")).Trim());
    }

    [Fact]
    public async Task Cancellation_kills_the_worker_and_the_pool_recovers()
    {
        using var runner = new PowerShellRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync("Start-Sleep 30", ct: cts.Token));
        Assert.Equal("ok", (await runner.RunAsync("'ok'")).Trim());
    }


    // Three parallel scripts that all finish at the same instant, however long each worker takes to start
    private static Task FillPoolAsync(PowerShellRunner runner)
    {
        var until = DateTime.UtcNow.AddSeconds(8).ToString("o");
        var script = $"while ([DateTime]::UtcNow -lt [DateTime]::Parse('{until}').ToUniversalTime()) {{ Start-Sleep -Milliseconds 20 }}; 'x'";
        return Task.WhenAll(Enumerable.Range(0, 3).Select(_ => runner.RunAsync(script)));
    }

    [Fact]
    public async Task Only_one_warm_worker_is_kept_after_inactivity()
    {
        using var runner = new PowerShellRunner(idleTrimDelay: TimeSpan.FromSeconds(2));
        await FillPoolAsync(runner);
        Assert.Equal(3, runner.IdleWorkerCount);

        for (int i = 0; i < 100 && runner.IdleWorkerCount > 1; i++) await Task.Delay(100);
        Assert.Equal(1, runner.IdleWorkerCount);
        Assert.Equal("ok", (await runner.RunAsync("'ok'")).Trim());   // the kept worker still answers
    }

    [Fact]
    public async Task Regular_polling_on_one_worker_does_not_keep_the_others()
    {
        using var runner = new PowerShellRunner(idleTrimDelay: TimeSpan.FromSeconds(1));
        await FillPoolAsync(runner);
        var pids = new HashSet<string>();
        for (int i = 0; i < 30; i++)               // poll every 100 ms for 3 s, like the VM page (every 5 s)
        {
            pids.Add((await runner.RunAsync("$PID")).Trim());
            await Task.Delay(100);
        }
        Assert.Single(pids);                       // always the most recently used worker
        for (int i = 0; i < 50 && runner.IdleWorkerCount > 1; i++) await Task.Delay(100);
        Assert.Equal(1, runner.IdleWorkerCount);
    }

    [Fact]
    public async Task Parallel_calls_run_on_separate_workers()
    {
        using var runner = new PowerShellRunner();
        var pids = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            runner.RunAsync("Start-Sleep -Milliseconds 300; $PID")));
        Assert.Equal(3, pids.Select(p => p.Trim()).Distinct().Count());
    }

    [Fact]
    public async Task Script_error_tokens_are_translated_in_the_exception()
    {
        var ex = await Assert.ThrowsAsync<PowerShellException>(() =>
            new PowerShellRunner().RunAsync(@"throw ""##Err_FileExists|C:\x.vhdx"""));
        Assert.Equal(LocalizationService.T("Err_FileExists", @"C:\x.vhdx"), ex.Message);
    }

    [Fact]
    public async Task Output_and_unicode_survive_the_stdin_bootstrap()
    {
        // ’ (U+2019) closes a single-quoted PowerShell string: Esc() is required
        var output = await new PowerShellRunner().RunAsync($"Write-Output '{PowerShellRunner.Esc("é ’ ok")}'");
        Assert.Equal("é ’ ok", output.Trim());
    }
}
