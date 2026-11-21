using System.Diagnostics;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.Core.Tests;

/// <summary>
/// Every PowerShell script the services generate, built from hostile values (quotes, $(), backticks, newlines),
/// must parse in the Windows PowerShell 5.1 the app runs: a quoting bug becomes a parse error here, not on a host.
/// </summary>
public class GeneratedScriptTests
{
    private const string Name   = "LAB-01 'x' $(calc) `t ;#";
    private const string Folder = @"D:\VMs\it's $(x)";
    private const string Switch = "LAN 'q' $(x)";

    private static async Task Capture(Func<Task> call)
    {
        try { await call(); }
        catch (Exception) { /* the fake runner returns no JSON: only the script matters here */ }
    }

    private static async Task<List<(string Label, string Script)>> AllScriptsAsync()
    {
        var runner = new FakePowerShellRunner(_ => "");
        var hv   = new HyperVService(runner);
        var vhdx = new VhdxService(runner);
        var nat  = new NatService(runner);
        var id   = Guid.NewGuid().ToString();
        var labels = new List<string>();

        async Task Run(string label, Func<Task> call)
        {
            var before = runner.Scripts.Count;
            await Capture(call);
            for (var i = before; i < runner.Scripts.Count; i++) labels.Add(label);
        }

        var before = new VmSettings { SwitchName = "OLD" };
        var after = before.Clone();
        after.ProcessorCount = 4; after.Nested = true; after.StartupMB = 4096; after.DynamicMemory = true;
        after.MinimumMB = 1024; after.MaximumMB = 8192; after.IsoPath = @"D:\ISO\it's $(x).iso"; after.SwitchName = Switch;
        after.VlanMode = VmSettings.VlanTrunk; after.NativeVlanId = 1; after.AllowedVlans = "10,20-30";
        after.DhcpGuard = true; after.RouterGuard = true; after.MacSpoofing = true;

        var notes  = VmNotes.Build(DateTime.Now, "Administrator", "P@ss'\"$(x)`n");
        var answer = AnswerFileGenerator.Generate(new AnswerFileConfig { ComputerName = "LAB-01", AdminPassword = "P@ss'\"$(x)<&>" });
        var seed   = CloudInitGenerator.Generate(new CloudInitConfig { HostName = "lab-01", Password = "p'\"$x`", SshPublicKey = "ssh-ed25519 AAAA x'y" });

        await Run("delete plan (id)",  () => hv.GetDeletePlanAsync(id));
        await Run("delete plan (name)",() => hv.GetDeletePlanAsync(Name));
        await Run("remove",            () => hv.RemoveVMAsync(id, [@"D:\VMs\a'b\x.vhdx"]));
        await Run("checkpoint",        () => hv.CreateCheckpointAsync(id, "cp 'a' $(b)"));
        await Run("apply checkpoint",  () => hv.ApplyCheckpointAsync(id, Guid.NewGuid().ToString()));
        await Run("remove checkpoint", () => hv.RemoveCheckpointAsync(Name, Guid.NewGuid().ToString(), true));
        await Run("rename checkpoint", () => hv.RenameCheckpointAsync(id, Guid.NewGuid().ToString(), "new 'n' $(x)"));
        await Run("get settings",      () => hv.GetVmSettingsAsync(id));
        await Run("set settings",      () => hv.SetVmSettingsAsync(id, before, after));
        await Run("set notes",         () => hv.SetNotesAsync(id, VmNotes.WithTags(notes, ["web'x", "$(y)"])));
        await Run("group",             () => hv.AddToGroupAsync("G'1 $(x)", [id, Name]));
        await Run("ungroup",           () => hv.RemoveFromGroupsAsync([id, Name]));
        await Run("external switch",   () => hv.CreateExternalSwitchAsync(Switch, "Ethernet 'x'", 10));
        await Run("internal switch",   () => hv.CreateInternalSwitchAsync(Switch, 20));
        await Run("private switch",    () => hv.CreatePrivateSwitchAsync(Switch));
        await Run("remove switch",     () => hv.RemoveVSwitchAsync(Switch));
        await Run("remove switch (id)",() => hv.RemoveVSwitchAsync(id));
        await Run("host cards",        hv.GetHostAdaptersAsync);
        await Run("add host card",     () => hv.AddHostAdapterAsync(id, "LAN VLAN.100", 100, dhcp: false));
        await Run("add host card",     () => hv.AddHostAdapterAsync(Switch, "LAN", 0, dhcp: true));
        await Run("host card vlan",    () => hv.SetHostAdapterVlanAsync("Microsoft:x'y\\z", 0));
        await Run("host card dhcp",    () => hv.SetHostAdapterDhcpAsync("Microsoft:x'y\\z", false));
        await Run("remove host card",  () => hv.RemoveHostAdapterAsync("Microsoft:x'y\\z"));
        await Run("switch type (id)",  () => hv.ChangeSwitchTypeAsync(id, "Private", null));
        await Run("switch type",       () => hv.ChangeSwitchTypeAsync(Switch, "External", "Ethernet 'x'"));
        await Run("switch type",       () => hv.ChangeSwitchTypeAsync(Switch, "Internal", null));
        await Run("ics status",        hv.GetIcsStatusAsync);
        await Run("ics on",            () => hv.EnableIcsAsync("Wi-Fi 'x' $(y)", Switch));
        await Run("ics off",           hv.DisableIcsAsync);
        await Run("enhanced session",  () => hv.SetEnhancedSessionHostAsync(true));
        await Run("blank vm",          () => hv.CreateBlankVmAsync(Name, Switch, 2048, 2, 2, Folder, 40, true, true, notes));
        await Run("windows clone",     () => hv.CreateVMWithDifferencingDiskAsync(Name, @"D:\Bases\BASE_x'y.vhdx", Switch, 2048, 2, 2,
                                                Folder, answer, true, false, notes));
        await Run("windows static ip", () => hv.CreateVMWithDifferencingDiskAsync(Name, @"D:\Bases\BASE_x'y.vhdx", Switch, 2048, 2, 2,
                                                Folder, answer, true, false, notes, staticMac: "00-15-5D-0A-0B-0C"));
        await Run("linux clone",       () => hv.CreateVMWithDifferencingDiskAsync(Name, @"D:\Bases\BASE_LinuxAlma_9.6.0.0_40.vhdx", Switch,
                                                2048, 2, 2, Folder, null, true, false, notes,
                                                BaseImageInfo.LinuxSecureBootTemplate, seed));
        await Run("nat",               () => nat.CreateNatNetworkAsync(Switch, "NAT'1 $(x)", "192.168.50.1", 24));
        await Run("remove nat",        () => nat.RemoveNatNetworkAsync("NAT'1 $(x)"));
        await Run("wim images",        () => vhdx.GetWimImagesAsync(@"D:\ISO\it's\install.wim"));
        await Run("new base",          () => vhdx.CreateBaseVhdxAsync(@"D:\Bases\it's $(x).vhdx", 60));
        await Run("discard base",      () => vhdx.DiscardPartialVhdxAsync(@"D:\Bases\it's.vhdx"));
        await Run("detach",            () => vhdx.EnsureDetachedAsync(@"D:\Bases\it's.vhdx"));
        await Run("apply image",       () => vhdx.ApplyWindowsImageAsync(@"D:\Bases\it's.vhdx", @"D:\ISO\it's\install.wim", 2, 2, answer));
        await Run("base dependents",   vhdx.GetBaseDependentsAsync);
        await Run("run in guest",      () => hv.InvokeInGuestAsync(id, "Administrator", "P@ss'\"$(x)", "Get-Date; 'a''b' | Out-Host\n$x = \"$(1+1)\""));
        await Run("copy to guest",     () => hv.CopyToGuestAsync(id, @"D:\Tools\it's $(x).ps1", @"C:\HV-LAB"));
        await Run("copy vm disk",      () => vhdx.CopyVmDiskAsync(Name, @"D:\Bases\it's.vhdx"));
        await Run("analyze vhdx",      () => vhdx.AnalyzeVhdxAsync(@"E:\Images\it's $(x).vhdx"));
        await Run("copy vhdx file",    () => vhdx.CopyVhdxFileAsync(@"E:\Images\it's $(x).vhd", @"D:\Bases\~building_x.vhdx"));

        // Scripts run inside a guest (PowerShell Direct carries them as base64): parsed as they are
        return labels.Zip(runner.Scripts)
                     .Append(("dc feature", LabBlueprintService.FeatureScript))
                     .Append(("dc promotion", LabBlueprintService.PromotionScript("lab.local", "P@ss'\"$(x)`")))
                     .ToList();
    }

    [Fact]
    public async Task Every_generated_script_is_built()
    {
        var scripts = await AllScriptsAsync();
        Assert.True(scripts.Count >= 30, $"only {scripts.Count} scripts were generated");
    }

    [Fact]
    public async Task Every_generated_script_parses_in_windows_powershell()
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        if (!File.Exists(powershell)) return;   // not a Windows host with PowerShell 5.1

        var scripts = await AllScriptsAsync();
        var dir = Directory.CreateTempSubdirectory("hvlab-parse-").FullName;
        try
        {
            for (var i = 0; i < scripts.Count; i++)
                File.WriteAllText(Path.Combine(dir, $"{i:D3}.ps1"), scripts[i].Script, new System.Text.UTF8Encoding(true));

            // One process parses them all: each error comes back as "<file>|<line>|<message>"
            var check = Path.Combine(dir, "check.ps1");
            File.WriteAllText(check, """
                param([string]$Dir)
                $files = @(Get-ChildItem -LiteralPath $Dir -Filter '*.ps1' | Where-Object { $_.BaseName -match '^\d{3}$' })
                foreach ($f in $files) {
                    $errors = $null
                    [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$errors)
                    foreach ($e in $errors) { "$($f.BaseName)|$($e.Extent.StartLineNumber)|$($e.Message)" }
                }
                "parsed|$($files.Count)|$($PSVersionTable.PSVersion.Major)"
                """);
            var psi = new ProcessStartInfo(powershell)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", check, "-Dir", dir })
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Contains($"parsed|{scripts.Count}|5", lines);   // every file went through the 5.1 parser
            var failures = lines
                .Select(line => line.Split('|', 3))
                .Where(p => p.Length == 3 && int.TryParse(p[0], out _))
                .Select(p => $"{scripts[int.Parse(p[0])].Label} (line {p[1]}): {p[2]}")
                .ToList();
            Assert.True(failures.Count == 0, "Scripts that do not parse:\n" + string.Join("\n", failures));
        }
        finally { Directory.Delete(dir, true); }
    }
}
