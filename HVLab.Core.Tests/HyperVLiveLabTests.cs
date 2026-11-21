using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using HVLab.Models;
using HVLab.Services;
using HVLab.Services.Wmi;

namespace HVLab.Core.Tests;

/// <summary>
/// Runs only with HVLAB_LIVE=3, elevated, on a Hyper-V host with a Windows base image in the HV-LAB base folder
/// (never in CI). These tests boot real guests from the bases: they take several minutes.
/// </summary>
public sealed class LiveGuestFactAttribute : FactAttribute
{
    public LiveGuestFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HVLAB_LIVE") != "3")
            Skip = "Hyper-V live test that boots guests: set HVLAB_LIVE=3 and run elevated (several minutes)";
        else if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "Hyper-V live test: needs elevation";
        else if (!Directory.Exists(HyperVLiveLabTests.BasesFolder)
                 || !Directory.EnumerateFiles(HyperVLiveLabTests.BasesFolder, "BASE_Windows*.vhdx").Any())
            Skip = $"Hyper-V live test: no BASE_Windows*.vhdx in '{HyperVLiveLabTests.BasesFolder}' (set HVLAB_LIVE_BASES)";
    }
}

/// <summary>
/// The lab features end to end on real guests: a blueprint deployed from the bases (switch, NAT, Windows and Linux VMs
/// with static IPs, group, lab defaults), PowerShell Direct and Copy-VMFile in the Windows guest, the Linux guest on
/// its static IP, a lab checkpoint, the export back to a blueprint, and a cancelled creation that leaves nothing
/// behind. Only HVLT-* objects are created, and they are removed even when a test fails.
/// </summary>
[Collection("Hyper-V changes")]
public class HyperVLiveLabTests
{
    private static readonly HyperVWmi Wmi = new();
    private static readonly PowerShellRunner Ps = new();
    private static readonly HyperVService Hv = new(Ps, Wmi);
    private static readonly NatService Nat = new(Ps, Wmi);
    private static readonly VhdxService Vhdx = new(Ps);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HV-LAB", "livetests");

    // A /24 no other test uses (the NAT test takes 172.31.254.0/28)
    private const string Gateway = "172.31.253.1", WinIp = "172.31.253.10", LinuxIp = "172.31.253.11",
                         LinuxRunCmdIp = "172.31.253.12";

    /// <summary>Windows setup from a base (specialize, OOBE) until the administrator can log on.</summary>
    private static readonly TimeSpan GuestReady = TimeSpan.FromMinutes(25);

    /// <summary>The base images folder: HVLAB_LIVE_BASES, else the one of the HV-LAB settings (read, never written).</summary>
    internal static string BasesFolder
    {
        get
        {
            if (Environment.GetEnvironmentVariable("HVLAB_LIVE_BASES") is { Length: > 0 } folder) return folder;
            foreach (var file in new[] { AppData.SettingsFile, AppData.LegacySettingsFile })
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    return json.RootElement.GetProperty("BaseImagesFolder").GetString() ?? "";
                }
                catch (Exception) { /* next one */ }
            }
            return "";
        }
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..6];

    private static async Task<VirtualMachine> GetAsync(string name)
        => (await Hv.GetVirtualMachinesAsync()).SingleOrDefault(v => v.Name == name)
           ?? throw new Xunit.Sdk.XunitException($"VM {name} not found");

    /// <summary>The Windows base that boots fastest: a Server Core, else the smallest Windows Server, else any Windows.</summary>
    private static BaseVhdx PickWindowsBase(IEnumerable<BaseVhdx> bases)
        => bases.Where(b => b.IsBuilt && !b.IsLinux && b.OsIdentifier.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(b => b.OsIdentifier.Contains("Core", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(b => b.OsIdentifier.Contains("Server", StringComparison.OrdinalIgnoreCase))
                .ThenBy(b => new FileInfo(b.FilePath).Length)
                .First();

    /// <summary>Retries <paramref name="attempt"/> until it succeeds; past <paramref name="timeout"/> its error is reported.</summary>
    private static async Task<T> UntilAsync<T>(Func<Task<T>> attempt, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try { return await attempt(); }
            catch (Exception) when (DateTime.UtcNow < deadline) { await Task.Delay(TimeSpan.FromSeconds(15)); }
        }
    }

    /// <summary>Removes the HVLT objects of a test, whatever state the test left them in; a failed step never hides the test's error.</summary>
    private static async Task CleanupAsync(IEnumerable<string> vms, string? sw = null, string? nat = null, string? group = null)
    {
        async Task Try(string script)
        {
            try { await Ps.RunAsync(script); }
            catch (Exception) { /* best effort: the next steps still run */ }
        }
        foreach (var vm in vms)
            await Try($"Get-VM | Where-Object Name -eq '{PowerShellRunner.Esc(vm)}' | ForEach-Object {{ " +
                      "Stop-VM -VM $_ -TurnOff -Force -ErrorAction SilentlyContinue; Remove-VM -VM $_ -Force }; " +
                      $"Remove-Item -LiteralPath (Join-Path '{PowerShellRunner.Esc(Folder)}' '{PowerShellRunner.Esc(vm)}') -Recurse -Force -ErrorAction SilentlyContinue");
        if (group is not null) await Try($"Get-VMGroup -Name '{PowerShellRunner.Esc(group)}' -ErrorAction SilentlyContinue | Remove-VMGroup -Force");
        if (nat is not null) await Try($"Remove-NetNat -Name '{PowerShellRunner.Esc(nat)}' -Confirm:$false -ErrorAction SilentlyContinue");
        if (sw is not null) await Try($"Remove-VMSwitch -Name '{PowerShellRunner.Esc(sw)}' -Force -ErrorAction SilentlyContinue");
    }

    private sealed class Callback(Action<string> onLine) : IProgress<string>
    {
        public void Report(string value) => onLine(value);
    }

    [LiveGuestFact]
    public async Task A_windows_vhdx_is_copied_then_read_like_an_import()
    {
        var b = PickWindowsBase(await Vhdx.GetBaseVhdxListAsync(BasesFolder));
        Directory.CreateDirectory(Folder);
        var copy = Path.Combine(Folder, $"HVLT-IMPORT-{Tag()}.vhdx");
        try
        {
            Assert.True(await Vhdx.CopyVhdxFileAsync(b.FilePath, copy) > 0);
            var read = await Vhdx.AnalyzeVhdxAsync(copy);
            Assert.True(read.IsWindows, "no Windows found on the copy");
            Assert.Equal((2, "Dynamic"), (read.Generation, read.VhdType));
            Assert.True(read.IsGeneralized, $"ImageState: '{read.ImageState}'");     // a base never booted is sysprepped
            Assert.StartsWith(string.Join('.', b.OsVersion.Split('.').Take(3)), read.WindowsVersion);
            Assert.Equal("False", (await Ps.RunAsync($"(Get-VHD -Path '{PowerShellRunner.Esc(copy)}').Attached")).Trim());   // dismounted

            // Disks dropped in the bases folder without a BASE_ name (downloaded images): read only, and reported in
            // livetests\import-report.txt, the way the import page would show them
            var report = new List<string>();
            foreach (var file in Directory.EnumerateFiles(BasesFolder, "*.vhd*")
                                          .Where(f => !Path.GetFileName(f).StartsWith("BASE_", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var a = await Vhdx.AnalyzeVhdxAsync(file);
                    report.Add($"{Path.GetFileName(file)} | gen {a.Generation} | {a.SizeGB} GB | {a.VhdType} | " +
                               $"windows={a.IsWindows} {a.WindowsVersion} | state={a.ImageState} | sysprepped={a.IsGeneralized}");
                }
                catch (Exception ex) { report.Add($"{Path.GetFileName(file)} | ERROR {ex.Message}"); }
            }
            File.WriteAllLines(Path.Combine(Folder, "import-report.txt"), report);
        }
        finally
        {
            await Ps.RunAsync($"Dismount-DiskImage -ImagePath '{PowerShellRunner.Esc(copy)}' -ErrorAction SilentlyContinue | Out-Null; " +
                              $"Remove-Item -LiteralPath '{PowerShellRunner.Esc(copy)}' -Force -ErrorAction SilentlyContinue");
        }
    }

    [LiveGuestFact]
    public async Task A_cancelled_creation_leaves_nothing_behind()
    {
        var tag = Tag(); var sw = $"HVLT-SW-{tag}"; var name = $"HVLT-CXL-{tag}";
        var b = PickWindowsBase(await Vhdx.GetBaseVhdxListAsync(BasesFolder));
        using var cts = new CancellationTokenSource();
        // Cancelled 1 s into the unattend injection, while the differencing disk is mounted in the host
        var progress = new Callback(line => { if (line.Contains("Prg_Inject")) cts.CancelAfter(TimeSpan.FromSeconds(1)); });
        try
        {
            await Hv.CreatePrivateSwitchAsync(sw);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Hv.CreateVMWithDifferencingDiskAsync(
                name, b.FilePath, sw, 1024, 1, b.Generation ?? 2, Folder, "<unattend/>", progress: progress, ct: cts.Token));

            Assert.DoesNotContain(await Hv.GetVirtualMachinesAsync(), v => v.Name == name);
            Assert.False(Directory.Exists(Path.Combine(Folder, name)), "the VM folder is left behind");
            // The differencing disk is no longer attached to the host
            Assert.Equal("0", (await Ps.RunAsync(
                $"@(Get-Disk | Where-Object Location -like '*{PowerShellRunner.Esc(name)}*').Count")).Trim());
        }
        finally { await CleanupAsync([name], sw: sw); }
    }

    [LiveGuestFact]
    public async Task A_domain_controller_is_promoted_and_its_member_joins()
    {
        var tag = Tag();
        var server = (await Vhdx.GetBaseVhdxListAsync(BasesFolder))
                         .Where(b => b.IsBuilt && !b.IsLinux && b.OsIdentifier.Contains("Server", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(b => b.OsIdentifier.Contains("Core", StringComparison.OrdinalIgnoreCase))
                         .ThenBy(b => new FileInfo(b.FilePath).Length)
                         .FirstOrDefault() ?? throw new Xunit.Sdk.XunitException($"no Windows Server base in {BasesFolder}");
        var domain = $"hvlt{tag}.lab";
        var domainAdmin = $@"{LabBlueprintService.NetBiosName(domain)}\Administrator";
        // The member is listed first: the controller is created and promoted before it anyway
        var lab = new LabBlueprint
        {
            Name = $"HVLT-AD-{tag}", Switch = $"HVLT-ADSW-{tag}",
            Nat = new LabNat { Gateway = "172.31.252.1", PrefixLength = 24 },
            Password = $"Hv!{tag}-Lab9",
            Domain = new LabDomain { Name = domain },
            Vms =
            [
                new LabVm { Name = $"HVLT-M-{tag}", Base = server.OsIdentifier, Ip = "172.31.252.11", Join = true },
                new LabVm { Name = $"HVLT-DC-{tag}", Base = server.OsIdentifier, Ip = "172.31.252.10", DomainController = true },
            ],
        };
        var names = lab.Vms.Select(v => v.Name).ToList();
        var svc = new LabBlueprintService(Hv, Nat, Vhdx) { BaseFolder = () => BasesFolder, VmsFolder = () => Folder };
        try
        {
            Assert.Null(lab.Validate());
            await svc.DeployAsync(lab);
            var dc = await GetAsync(names[1]);
            Assert.Equal("Running", dc.State);

            // The member joins the domain during its first boot (its DNS: the controller)
            var member = await GetAsync(names[0]);
            Assert.Equal("Off", member.State);
            await Hv.PowerAsync(member.Id, VmPowerAction.Start);
            await UntilAsync(async () =>
            {
                var joined = await Hv.InvokeInGuestAsync(member.Id, domainAdmin, lab.Password, "(Get-CimInstance Win32_ComputerSystem).Domain");
                return joined.Equals(domain, StringComparison.OrdinalIgnoreCase) ? joined : throw new InvalidOperationException($"domain: {joined}");
            }, GuestReady);

            // The directory lists it
            var computers = await Hv.InvokeInGuestAsync(dc.Id, domainAdmin, lab.Password, "(Get-ADComputer -Filter *).Name -join ','");
            Assert.Contains(InputValidator.ToComputerName(names[0]), computers, StringComparison.OrdinalIgnoreCase);
        }
        finally { await CleanupAsync(names, sw: lab.Switch, nat: $"{lab.Name}-NAT", group: lab.Name); }
    }

    [LiveGuestFact]
    public async Task A_lab_deploys_boots_and_answers_through_powershell_direct()
    {
        var tag = Tag();
        var bases = await Vhdx.GetBaseVhdxListAsync(BasesFolder);
        var win = PickWindowsBase(bases);
        var linux = bases.FirstOrDefault(b => b.IsBuilt && b.IsLinux);
        var lab = new LabBlueprint
        {
            Name = $"HVLT-LAB-{tag}", Switch = $"HVLT-LABSW-{tag}",
            Nat = new LabNat { Gateway = Gateway, PrefixLength = 24 },
            Password = $"Hv!{tag}-Lab9",
            Vms = [new LabVm { Name = $"HVLT-W-{tag}", Base = win.OsIdentifier, Ip = WinIp }],
        };
        // Its first-boot command adds a second address: SSH answering there proves that runcmd ran
        if (linux is not null)
            lab.Vms.Add(new LabVm { Name = $"HVLT-L-{tag}", Base = linux.OsIdentifier, Ip = LinuxIp, User = "hvlt",
                                    Commands = [$"ip addr add {LinuxRunCmdIp}/24 dev eth0"] });
        var names = lab.Vms.Select(v => v.Name).ToList();
        var svc = new LabBlueprintService(Hv, Nat, Vhdx) { BaseFolder = () => BasesFolder, VmsFolder = () => Folder };
        var file = Path.Combine(Folder, $"hvlt-{tag}.txt");
        try
        {
            // Deploy, then a second plan finds everything: deploying again would create nothing
            Assert.Null(lab.Validate());
            Assert.All(await svc.PlanAsync(lab), s => Assert.Equal(LabBlueprintService.Create, s.Action));
            await svc.DeployAsync(lab);
            Assert.All(await svc.PlanAsync(lab), s => Assert.Equal(LabBlueprintService.Exists, s.Action));

            // Group, notes, lab defaults and the static MAC of the static IP
            var w = await GetAsync(names[0]);
            Assert.Equal(lab.Name, w.GroupName);
            Assert.Equal(WinIp, w.Credentials.IpAddress);
            Assert.Equal("False|ShutDown|True|False", (await Ps.RunAsync($$"""
                $vm  = Get-VM -Id '{{w.Id}}'
                $gsi = (Get-VMIntegrationService -VM $vm | Where-Object { $_.Id -match '6C09BB55-D683-4DA0-8931-C9BF705F6480' }).Enabled
                $nic = Get-VMNetworkAdapter -VM $vm | Select-Object -First 1
                "$($vm.AutomaticCheckpointsEnabled)|$($vm.AutomaticStopAction)|$gsi|$($nic.DynamicMacAddressEnabled)"
                """)).Trim());

            foreach (var n in names) await Hv.PowerAsync((await GetAsync(n)).Id, VmPowerAction.Start);

            // Windows: PowerShell Direct with the credentials of the notes, once setup has set the password;
            // the guest has its computer name and the static IP, gateway and DNS of the blueprint
            var creds = w.Credentials;
            var network = await UntilAsync(() => Hv.InvokeInGuestAsync(w.Id, creds.Account, creds.Password, $$"""
                $ip  = Get-NetIPAddress -AddressFamily IPv4 -IPAddress '{{WinIp}}' -ErrorAction Stop
                $gw  = (Get-NetRoute -DestinationPrefix '0.0.0.0/0' -InterfaceIndex $ip.InterfaceIndex -ErrorAction Stop | Select-Object -First 1).NextHop
                $dns = (Get-DnsClientServerAddress -AddressFamily IPv4 -InterfaceIndex $ip.InterfaceIndex).ServerAddresses -join ','
                "$env:COMPUTERNAME|$($ip.PrefixLength)|$gw|$dns"
                """), GuestReady);
            Assert.Equal($"{InputValidator.ToComputerName(names[0])}|24|{Gateway}|{Gateway}", network, ignoreCase: true);

            // Copy-VMFile into the guest, read back through PowerShell Direct
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(file, tag);
            await Hv.CopyToGuestAsync(w.Id, file, @"C:\HVLT");
            Assert.Equal(tag, await Hv.InvokeInGuestAsync(w.Id, creds.Account, creds.Password,
                $@"Get-Content -LiteralPath 'C:\HVLT\hvlt-{tag}.txt'"));

            // Linux: cloud-init gave the clone its static IP, and its SSH server answers there, then on the
            // address its first-boot command added
            if (linux is not null)
                foreach (var address in new[] { LinuxIp, LinuxRunCmdIp })
                    await UntilAsync(async () =>
                    {
                        using var tcp = new TcpClient();
                        await tcp.ConnectAsync(address, 22).WaitAsync(TimeSpan.FromSeconds(5));
                        return true;
                    }, TimeSpan.FromMinutes(10));

            // Lab checkpoint, as the VM list takes it: running VMs paused, one checkpoint each, resumed
            var cp = $"HVLT-CP-{tag}";
            var ids = new List<string>();
            foreach (var n in names) ids.Add((await GetAsync(n)).Id);
            foreach (var id in ids) await Hv.PowerAsync(id, VmPowerAction.Pause);
            foreach (var id in ids) await Hv.CreateCheckpointAsync(id, cp);
            foreach (var id in ids) await Hv.PowerAsync(id, VmPowerAction.Resume);
            foreach (var id in ids)
            {
                Assert.Contains(await Hv.GetCheckpointsAsync(id), c => c.Name == cp);
                Assert.Equal("Running", (await Hv.GetVirtualMachinesAsync()).Single(v => v.Id == id).State);
            }

            // Export back to a blueprint: same switch, NAT, bases, sizes and static IPs
            var exported = await svc.ExportAsync(lab.Name, await Task.WhenAll(names.Select(GetAsync)));
            Assert.Equal(lab.Switch, exported.Switch);
            Assert.Equal(Gateway, exported.Nat?.Gateway);
            Assert.Equal(24, exported.Nat?.PrefixLength);
            Assert.Equal(lab.Vms.Count, exported.Vms.Count);
            for (var i = 0; i < lab.Vms.Count; i++)
            {
                var (sent, back) = (lab.Vms[i], exported.Vms[i]);
                Assert.Equal(sent.Name, back.Name);
                Assert.Equal(sent.Base + "*", back.Base);
                Assert.Equal(sent.Ip, back.Ip);
                Assert.Equal(sent.MemoryMB, back.MemoryMB);
                Assert.Equal(sent.Cpu, back.Cpu);
            }
        }
        finally
        {
            await CleanupAsync(names, sw: lab.Switch, nat: $"{lab.Name}-NAT", group: lab.Name);
            File.Delete(file);
        }
    }
}
