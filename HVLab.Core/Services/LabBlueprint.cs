using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Serialization;
using HVLab.Models;
using static HVLab.Services.LocalizationService;

namespace HVLab.Services;

/// <summary>
/// A whole lab in one JSON file: its switch (and NAT), and its VMs with their base image, size, static IP, and
/// domain join. Deploying it creates only what is missing, then puts the VMs in a Hyper-V group named after the lab.
/// </summary>
public sealed class LabBlueprint
{
    public string Name { get; set; } = "";
    /// <summary>Switch of every VM: used when it exists, else created as an internal switch.</summary>
    public string Switch { get; set; } = "";
    /// <summary>NAT created with the switch (gateway = the host's address on it).</summary>
    public LabNat? Nat { get; set; }
    /// <summary>Administrator (Windows) or user (Linux) password of every VM, also used to join the domain.</summary>
    public string Password { get; set; } = "";
    public string Language { get; set; } = "fr-FR";
    public string TimeZone { get; set; } = "Romance Standard Time";
    public LabDomain? Domain { get; set; }
    public List<LabVm> Vms { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static LabBlueprint Parse(string json)
        => JsonSerializer.Deserialize<LabBlueprint>(json, Json) ?? throw new InvalidDataException(T("LAB_Empty"));

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// The first problem of the file, localized; null when it can be deployed. A draft may be saved without its
    /// password (<paramref name="requirePassword"/> false): only deploying needs it.
    /// </summary>
    public string? Validate(bool requirePassword = true)
    {
        if (string.IsNullOrWhiteSpace(Name)) return T("LAB_ErrName");
        if (string.IsNullOrWhiteSpace(Switch)) return T("LAB_ErrSwitch");
        if (Vms.Count == 0) return T("LAB_ErrNoVm");
        if (requirePassword && string.IsNullOrEmpty(Password)) return T("Val_PasswordEmpty");
        if (Nat is { } nat && NatService.NormalizeGateway(nat.Gateway, nat.PrefixLength) is null) return T("SW_ErrInvalidGateway");
        if (Domain is { } d && InputValidator.CheckDomainName(d.Name) is { } dn) return T(dn);
        foreach (var vm in Vms)
        {
            if (InputValidator.CheckVmName(vm.Name) is { } key) return $"{T(key)} ({vm.Name})";
            if (string.IsNullOrWhiteSpace(vm.Base)) return T("LAB_ErrBase", vm.Name);
            if (vm.Ip is { Length: > 0 } ip && InputValidator.ParseIpv4(ip) is null) return T("LAB_ErrIp", vm.Name);
            if (vm.Ip is { Length: > 0 } && Gateway is null) return T("LAB_ErrGateway", vm.Name);
            if (InputValidator.CheckLinuxCommands(vm.Commands) is { } cmd) return $"{T(cmd)} ({vm.Name})";
        }
        if (Vms.Count(v => v.DomainController) > 1) return T("LAB_ErrTwoDc");
        if (DomainController is { } dc)
        {
            if (Domain is null) return T("LAB_ErrDcNoDomain", dc.Name);
            if (dc.Ip is not { Length: > 0 }) return T("LAB_ErrDcIp", dc.Name);
            if (dc.Join) return T("LAB_ErrDcJoin", dc.Name);
            if (requirePassword && !IsComplex(Password)) return T("LAB_ErrDcPassword");
        }
        var duplicate = Vms.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? null : T("LAB_ErrDuplicate", duplicate.Key);
    }

    /// <summary>
    /// The Windows rule for the domain administrator and the restore mode passwords: 8 characters or more, from at
    /// least three of uppercase, lowercase, digits and symbols.
    /// </summary>
    internal static bool IsComplex(string password)
        => password.Length >= 8
           && new[] { password.Any(char.IsUpper), password.Any(char.IsLower), password.Any(char.IsDigit),
                      password.Any(c => !char.IsLetterOrDigit(c)) }.Count(b => b) >= 3;

    /// <summary>Gateway of the static IPs: the NAT's.</summary>
    [JsonIgnore] public string? Gateway => Nat?.Gateway;
    [JsonIgnore] public int PrefixLength => Nat?.PrefixLength ?? 24;

    /// <summary>The VM promoted to the first domain controller of <see cref="Domain"/>, if any.</summary>
    [JsonIgnore] public LabVm? DomainController => Vms.FirstOrDefault(v => v.DomainController);

    /// <summary>
    /// DNS servers of a VM: its own, else the domain controller for the other VMs of a lab that has one (they find the
    /// domain), else the NAT gateway.
    /// </summary>
    public string? DnsOf(LabVm vm)
        => vm.Dns is { Length: > 0 } dns ? dns
         : DomainController is { Ip: { Length: > 0 } dcIp } dc && dc != vm ? dcIp
         : Gateway;
}

public sealed class LabNat
{
    public string Gateway { get; set; } = "";
    public int PrefixLength { get; set; } = 24;
}

public sealed class LabDomain
{
    public string Name { get; set; } = "";
    /// <summary>Account allowed to join (its password is the lab password).</summary>
    public string User { get; set; } = "Administrator";
    public string? Ou { get; set; }
}

public sealed class LabVm
{
    public string Name { get; set; } = "";
    /// <summary>OS identifier of the base, with * wildcards (WindowsServer2025*): the newest matching BASE_* is used.</summary>
    public string Base { get; set; } = "";
    public long MemoryMB { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    /// <summary>Static IPv4 on the lab NAT; empty for DHCP.</summary>
    public string? Ip { get; set; }
    /// <summary>DNS servers, comma-separated (the domain controller for its members); default: the NAT gateway.</summary>
    public string? Dns { get; set; }
    /// <summary>Windows: join the lab domain during setup.</summary>
    public bool Join { get; set; }
    /// <summary>
    /// Windows Server: deploying promotes it to the first domain controller of a new forest (AD DS and DNS), before
    /// the members are created.
    /// </summary>
    public bool DomainController { get; set; }
    /// <summary>Linux: the sudo user cloud-init creates.</summary>
    public string? User { get; set; }
    /// <summary>Free tags written into the VM notes (the VM list filters and searches them).</summary>
    public List<string>? Tags { get; set; }
    /// <summary>Linux: shell commands run once as root at the end of the first boot (cloud-init runcmd).</summary>
    public List<string>? Commands { get; set; }
}

/// <summary>One line of a deployment plan: what exists, what will be created, what cannot be.</summary>
public sealed record LabStep(string Kind, string Name, string Action, string Detail);

/// <summary>Plans, deploys and exports <see cref="LabBlueprint"/>s with the existing Hyper-V, NAT and base image services.</summary>
public sealed class LabBlueprintService(IHyperVService hv, INatService nat, IVhdxService vhdx)
{
    public const string Create = "create", Exists = "exists", Missing = "missing";

    /// <summary>Folders of the base images and of the new VMs (the Settings ones; set by the tests).</summary>
    public Func<string> BaseFolder { get; init; } = () => AppSettings.Current.BaseImagesFolder;
    public Func<string> VmsFolder  { get; init; } = () => AppSettings.Current.VmsFolder;

    /// <summary>The newest base whose OS identifier matches <paramref name="pattern"/> (wildcards *, case-insensitive).</summary>
    public static BaseVhdx? PickBase(IEnumerable<BaseVhdx> bases, string pattern)
        => bases.Where(b => b.IsBuilt && FileSystemName.MatchesSimpleExpression(pattern, b.OsIdentifier, ignoreCase: true))
                .OrderByDescending(b => Version.TryParse(b.OsVersion, out var v) ? v : new Version())
                .FirstOrDefault();

    public async Task<IReadOnlyList<LabStep>> PlanAsync(LabBlueprint lab)
    {
        var switches = await hv.GetVirtualSwitchesAsync();
        var vms      = await hv.GetVirtualMachinesAsync();
        var bases    = await vhdx.GetBaseVhdxListAsync(BaseFolder());
        var steps    = new List<LabStep>();
        var dc       = lab.DomainController;

        var switchExists = switches.Any(s => s.Name.Equals(lab.Switch, StringComparison.OrdinalIgnoreCase));
        steps.Add(new("switch", lab.Switch, switchExists ? Exists : Create, switchExists ? "" : T("LAB_InternalSwitch")));
        if (lab.Nat is { } n && !switchExists)
            steps.Add(new("nat", $"{lab.Name}-NAT", Create, NatService.NetworkPrefix(n.Gateway, n.PrefixLength)));
        foreach (var vm in lab.Vms)
        {
            var exists = vms.Any(v => v.Name.Equals(vm.Name, StringComparison.OrdinalIgnoreCase));
            if (exists)
                steps.Add(new("vm", vm.Name, Exists, ""));
            else if (PickBase(bases, vm.Base) is not { } b)
                steps.Add(new("vm", vm.Name, Missing, T("LAB_NoBase", vm.Base)));
            else if (vm == dc && !IsServer(b))
                steps.Add(new("vm", vm.Name, Missing, T("LAB_DcNeedsServer", vm.Name)));
            else
                steps.Add(new("vm", vm.Name, Create, $"{b.Name} · {vm.MemoryMB} MB · {vm.Cpu} vCPU{(vm.Ip is { Length: > 0 } ip ? " · " + ip : "")}"));
            // An existing DC is checked (and promoted if it is not yet) before members that join are created
            if (vm == dc && lab.Domain is { } d)
                steps.Add(new("domain", d.Name, exists ? Exists : Create, T(exists ? "LAB_DcChecked" : "LAB_DcPromote", vm.Name)));
        }
        return steps;
    }

    private static bool IsServer(BaseVhdx b) => !b.IsLinux && b.OsIdentifier.Contains("Server", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates what the plan says (switch, NAT, VMs in order), then groups the lab's VMs. A domain controller comes
    /// first: it is started and promoted before the members, which then join its domain during their own setup.
    /// </summary>
    public async Task DeployAsync(LabBlueprint lab, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var plan = await PlanAsync(lab);
        if (plan.FirstOrDefault(s => s.Action == Missing) is { } missing) throw new InvalidOperationException(missing.Detail);
        var bases = await vhdx.GetBaseVhdxListAsync(BaseFolder());

        if (plan.Any(s => s.Kind == "switch" && s.Action == Create))
        {
            progress?.Report(T("LAB_CreatingSwitch", lab.Switch));
            await hv.CreateInternalSwitchAsync(lab.Switch);
            if (lab.Nat is { } n)
            {
                progress?.Report(T("LAB_CreatingNat", n.Gateway));
                await nat.CreateNatNetworkAsync(lab.Switch, $"{lab.Name}-NAT", n.Gateway, n.PrefixLength);
            }
        }

        var toCreate = lab.Vms.Where(v => plan.Any(s => s.Kind == "vm" && s.Name == v.Name && s.Action == Create)).ToList();
        if (lab.DomainController is { } dc && lab.Domain is { } domain)
        {
            var newDc = toCreate.Remove(dc);
            if (newDc) await CreateVmAsync(lab, dc, bases, progress, ct);
            if (newDc || toCreate.Any(v => v.Join)) await EnsureDomainControllerAsync(lab, dc, domain, progress, ct);
        }
        foreach (var vm in toCreate) await CreateVmAsync(lab, vm, bases, progress, ct);

        progress?.Report(T("VM_Grouping", lab.Name));
        await hv.AddToGroupAsync(lab.Name, lab.Vms.Select(v => v.Name).ToList());
    }

    private async Task CreateVmAsync(LabBlueprint lab, LabVm vm, IReadOnlyList<BaseVhdx> bases, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(T("LAB_CreatingVm", vm.Name));
        var b = PickBase(bases, vm.Base)!;
        var ip = vm.Ip is { Length: > 0 } ? new StaticIpConfig(vm.Ip, lab.PrefixLength, lab.Gateway!,
                                                 InputValidator.ParseDnsServers(lab.DnsOf(vm)) ?? []) : null;
        if (b.IsLinux)
        {
            var user = vm.User ?? "labadmin";
            var seed = CloudInitGenerator.Generate(new CloudInitConfig
            {
                HostName = InputValidator.ToComputerName(vm.Name).ToLowerInvariant(), UserName = user, Password = lab.Password,
                TimeZone = lab.TimeZone, Language = lab.Language, StaticIp = ip,
                RunCommands = vm.Commands ?? [],
            });
            await hv.CreateVMWithDifferencingDiskAsync(vm.Name, b.FilePath, lab.Switch, vm.MemoryMB, vm.Cpu, b.Generation ?? 2,
                VmsFolder(), null, b.SecureBoot, false, VmNotes.Build(DateTime.Now, user, lab.Password, vm.Ip, tags: vm.Tags),
                b.SecureBootTemplate, seed, ct: ct);
            return;
        }

        var mac = ip is null ? null : InputValidator.NewHyperVMac();
        var answer = AnswerFileGenerator.Generate(new AnswerFileConfig
        {
            ComputerName = InputValidator.ToComputerName(vm.Name), AdminPassword = lab.Password,
            ProductKey = KmsKeyCatalog.SuggestForOs(b.OsIdentifier)?.Key ?? "",
            UILanguage = lab.Language, InputLocale = AnswerFileGenerator.GetInputLocale(lab.Language),
            SystemLocale = lab.Language, UserLocale = lab.Language, TimeZone = lab.TimeZone,
            OsFamily = b.OsIdentifier.Contains("server", StringComparison.OrdinalIgnoreCase) ? OsFamily.Server : OsFamily.Client,
            StaticIp = ip, MacAddress = mac ?? "",
            JoinDomain = vm.Join && !vm.DomainController && lab.Domain is { } d ? d.Name : "",
            DomainUser = lab.Domain?.User ?? "", DomainPassword = lab.Password, DomainOu = lab.Domain?.Ou ?? "",
        });
        await hv.CreateVMWithDifferencingDiskAsync(vm.Name, b.FilePath, lab.Switch, vm.MemoryMB, vm.Cpu, b.Generation ?? 2,
            VmsFolder(), answer, true, false,
            VmNotes.Build(DateTime.Now, AnswerFileGenerator.AdministratorAccount, lab.Password, vm.Ip, tags: vm.Tags),
            ct: ct, staticMac: mac);
    }

    // ─── Domain controller ──────────────────────────────────────────────────

    /// <summary>Pause between two tries while a guest boots or its services start (shortened by the tests).</summary>
    public TimeSpan GuestPoll { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>First boot of a Windows base (specialize, OOBE) until the administrator can log on.</summary>
    public TimeSpan GuestSetupTimeout { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Restart after the promotion until the directory answers.</summary>
    public TimeSpan DirectoryTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Starts the domain controller, waits for its setup, promotes it unless it already is one (a new forest with its
    /// DNS, through PowerShell Direct), then waits until the directory answers after the restart.
    /// </summary>
    private async Task EnsureDomainControllerAsync(LabBlueprint lab, LabVm dc, LabDomain domain, IProgress<string>? progress, CancellationToken ct)
    {
        var vm = (await hv.GetVirtualMachinesAsync()).FirstOrDefault(v => v.Name.Equals(dc.Name, StringComparison.OrdinalIgnoreCase))
                 ?? throw new InvalidOperationException(T("LAB_DcMissing", dc.Name));
        if (!vm.IsRunning)
        {
            progress?.Report(T("LAB_DcStarting", dc.Name));
            await hv.PowerAsync(vm.Id, vm.IsPaused ? VmPowerAction.Resume : VmPowerAction.Start);
        }

        var admin = AnswerFileGenerator.AdministratorAccount;
        var domainAdmin = $@"{NetBiosName(domain.Name)}\{admin}";
        // Win32_ComputerSystem.DomainRole: 4 and 5 are domain controllers
        progress?.Report(T("LAB_DcWaiting", dc.Name));
        var role = await UntilGuestAsync(dc.Name, vm.Id, [admin, domainAdmin], lab.Password,
            "(Get-CimInstance Win32_ComputerSystem).DomainRole", _ => true, GuestSetupTimeout, ct);
        if (!(int.TryParse(role, out var r) && r >= 4))
        {
            progress?.Report(T("LAB_DcPromoting", dc.Name, domain.Name));
            await hv.InvokeInGuestAsync(vm.Id, admin, lab.Password, FeatureScript);
            await hv.InvokeInGuestAsync(vm.Id, admin, lab.Password, PromotionScript(domain.Name, lab.Password));
        }

        progress?.Report(T("LAB_DcWaitingAd", dc.Name, domain.Name));
        await UntilGuestAsync(dc.Name, vm.Id, [domainAdmin], lab.Password,
            "(Get-ADDomain -Current LocalComputer -ErrorAction Stop).DNSRoot",
            root => root.Equals(domain.Name, StringComparison.OrdinalIgnoreCase), DirectoryTimeout, ct);
    }

    internal const string FeatureScript = "Install-WindowsFeature AD-Domain-Services -IncludeManagementTools -ErrorAction Stop | Out-Null";

    /// <summary>
    /// A new forest with its DNS server; the restart is left to shutdown.exe a few seconds later, so the PowerShell
    /// Direct session ends cleanly first. The restore mode password is the lab password.
    /// </summary>
    internal static string PromotionScript(string domain, string password) => $$"""
        $safe = ConvertTo-SecureString '{{PowerShellRunner.Esc(password)}}' -AsPlainText -Force
        Install-ADDSForest -DomainName '{{PowerShellRunner.Esc(domain)}}' -DomainNetbiosName '{{PowerShellRunner.Esc(NetBiosName(domain))}}' -SafeModeAdministratorPassword $safe -InstallDns -NoRebootOnCompletion -Force -WarningAction SilentlyContinue -ErrorAction Stop | Out-Null
        shutdown.exe /r /t 15 /c 'HV-LAB' | Out-Null
        """;

    /// <summary>lab.local → LAB: the first label in uppercase, 15 characters at most, as Windows proposes it.</summary>
    internal static string NetBiosName(string domain)
    {
        var label = domain.Split('.')[0].ToUpperInvariant();
        return label.Length > 15 ? label[..15] : label;
    }

    /// <summary>
    /// Runs <paramref name="script"/> in the guest until its output satisfies <paramref name="accept"/>, with each
    /// account in turn; past <paramref name="timeout"/> the last error is reported.
    /// </summary>
    private async Task<string> UntilGuestAsync(string name, string vmId, IReadOnlyList<string> accounts, string password,
                                               string script, Func<string, bool> accept, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = "";
        while (true)
        {
            foreach (var account in accounts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var output = (await hv.InvokeInGuestAsync(vmId, account, password, script)).Trim();
                    if (accept(output)) return output;
                    last = output;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { last = ex.Message; }
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(T("LAB_GuestTimeout", name, (int)timeout.TotalMinutes, last));
            await Task.Delay(GuestPoll, ct);
        }
    }

    /// <summary>
    /// A blueprint of existing VMs: their base (OS identifier of the parent base), size, switch and the static IP
    /// of their notes. The password is left empty: it is set before deploying it elsewhere.
    /// </summary>
    public async Task<LabBlueprint> ExportAsync(string name, IReadOnlyList<VirtualMachine> vms)
    {
        var bases = await vhdx.GetBaseVhdxListAsync(BaseFolder());
        var users = await vhdx.GetBaseDependentsAsync();
        string BaseOf(VirtualMachine v)
            => bases.FirstOrDefault(b => users.TryGetValue(b.FilePath, out var list) && list.Contains(v.Name)) is { } b
               ? (b.OsIdentifier.Length > 0 ? b.OsIdentifier : b.Name) + "*" : "";
        var gateway = (await nat.GetNatNetworksAsync()).FirstOrDefault(n => n.SwitchName == vms[0].SwitchName);
        return new LabBlueprint
        {
            Name = name,
            Switch = vms[0].SwitchName,
            Nat = gateway is null ? null : new LabNat
            {
                Gateway = HostGateway(gateway.InternalIPInterfaceAddressPrefix),
                PrefixLength = int.TryParse(gateway.InternalIPInterfaceAddressPrefix.Split('/').LastOrDefault(), out var p) ? p : 24,
            },
            Vms = vms.Select(v => new LabVm
            {
                // The startup RAM: a running VM with dynamic memory uses more or less than it was given
                Name = v.Name, Base = BaseOf(v), MemoryMB = v.StartupMemoryMB > 0 ? v.StartupMemoryMB : v.MemoryMB, Cpu = v.ProcessorCount,
                Ip = v.Credentials.IpAddress is { Length: > 0 } ip ? ip : null,
                Tags = v.Tags.Count > 0 ? v.Tags.ToList() : null,
            }).ToList(),
        };
    }

    /// <summary>A blueprint another page wants opened in the Labs editor (an export of the VM list).</summary>
    public string? RequestedFile { get; set; }

    /// <summary>Where blueprints are written (export, new template): &lt;VMs folder&gt;\Blueprints.</summary>
    public string BlueprintsFolder() => Path.Combine(VmsFolder(), "Blueprints");

    /// <summary>A free file name in the blueprints folder: name.json, name-2.json…</summary>
    public string FreePath(string name)
    {
        var folder = BlueprintsFolder();
        Directory.CreateDirectory(folder);
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        var path = Path.Combine(folder, $"{safe}.json");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{safe}-{i}.json");
        return path;
    }

    /// <summary>
    /// A commented starting point (DC, member server, Linux web server on a NAT) in the UI language, with the
    /// newest Windows Server and Linux bases of the folder as patterns. Its password is empty: deployment refuses it
    /// until it is filled in.
    /// </summary>
    public async Task<string> NewTemplateAsync()
    {
        var bases = await vhdx.GetBaseVhdxListAsync(BaseFolder());
        var server = bases.Where(b => !b.IsLinux && b.OsIdentifier.Contains("Server", StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(b => Version.TryParse(b.OsVersion, out var v) ? v : new Version()).FirstOrDefault();
        var linux  = bases.FirstOrDefault(b => b.IsLinux);
        var serverPattern = server is null ? "WindowsServer2025*" : server.OsIdentifier + "*";
        var linuxPattern  = linux  is null ? "LinuxAlma*"         : linux.OsIdentifier + "*";
        return T("LAB_Template", serverPattern, linuxPattern).Replace("[[", "{").Replace("]]", "}");
    }

    /// <summary>192.168.100.0/24 → 192.168.100.1, the address HV-LAB gives the host on its NAT switches.</summary>
    internal static string HostGateway(string prefix)
    {
        var network = InputValidator.ParseIpv4(prefix.Split('/')[0]);
        return network is { } n ? InputValidator.FormatIpv4(n + 1) : "";
    }
}
