using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

public partial class CreateVmViewModel : VmCreationViewModelBase
{
    // VM form
    [ObservableProperty] private long    memoryMB = 2048;
    [ObservableProperty] private int     cpuCount = 2;
    [ObservableProperty] private int     generation = 2;
    [ObservableProperty] private string  vmFolder;
    [ObservableProperty] private string  baseVhdxFolder;

    protected override string ImagesFolder => BaseVhdxFolder;

    protected override void OnBulkModeChangedCore(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(VmName))
            VmName = $"{BulkPrefix}-01";
    }

    // ─── Answer file (injected into the differencing disk at creation time) ────

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowWindowsOptions), nameof(ShowLinuxOptions), nameof(ShowStaticIpFields))]
    private bool useAnswerFile = true;
    [ObservableProperty] private string computerName   = "LAB-VM";
    [ObservableProperty] private string adminPassword  = "";
    [ObservableProperty] private bool   autoLogon      = false;
    [ObservableProperty] private string productKey     = "";
    [ObservableProperty] private KmsKeyEntry? selectedKmsKey;
    [ObservableProperty] private string uiLanguage     = "fr-FR";
    [ObservableProperty] private string timeZone       = "Romance Standard Time";
    [ObservableProperty] private string answerFilePreview = "";
    [ObservableProperty] private bool   buildCbsCache    = false;
    [ObservableProperty] private bool   disableWindowsUpdate = false;
    [ObservableProperty] private bool   showPassword     = false;

    public string PasswordEyeGlyph => ShowPassword ? "\uED1A" : "\uE052";

    partial void OnShowPasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordEyeGlyph));
        UpdateAnswerPreview();
    }

    partial void OnSelectedKmsKeyChanged(KmsKeyEntry? value)
    {
        if (value is not null)
            ProductKey = value.Key;
    }

    // ─── Linux base: a cloud-init seed instead of unattend.xml ───────────────────

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowWindowsOptions), nameof(ShowLinuxOptions), nameof(ShowStaticIpFields),
        nameof(AnswerSectionTitle), nameof(AnswerSectionInfo), nameof(PasswordLabel), nameof(PreviewTitle))]
    private bool isLinuxBase;

    [ObservableProperty] private string linuxUser    = "labadmin";
    [ObservableProperty] private string sshPublicKey = "";
    /// <summary>Linux: first-boot commands, one per line (cloud-init runcmd).</summary>
    [ObservableProperty] private string linuxCommands = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowStaticIpFields))] private bool useStaticIp;
    [ObservableProperty] private string ipAddress    = "";
    [ObservableProperty] private int    prefixLength = 24;
    [ObservableProperty] private string gateway      = "";
    [ObservableProperty] private string dnsServers   = "";

    public bool ShowWindowsOptions => UseAnswerFile && !IsLinuxBase;
    public bool ShowLinuxOptions   => UseAnswerFile && IsLinuxBase;
    /// <summary>Static IP for both families: cloud-init on Linux, unattend.xml (TCPIP, DNS) on Windows.</summary>
    public bool ShowStaticIpFields => UseAnswerFile && UseStaticIp;

    // ─── Windows: Active Directory join during setup ─────────────────────────

    [ObservableProperty] private bool   joinDomainEnabled;
    [ObservableProperty] private string domainName     = "";
    [ObservableProperty] private string domainUser     = "Administrator";
    [ObservableProperty] private string domainPassword = "";
    [ObservableProperty] private string domainOu       = "";

    public string AnswerSectionTitle => T(IsLinuxBase ? "CVM_CloudInit" : "CVM_AnswerFile");
    public string AnswerSectionInfo  => T(IsLinuxBase ? "CVM_CloudInitInfo" : "CVM_AnswerInfo");
    /// <summary>The account the password is for: the Linux user, Admins on Windows 10/11, Administrator on Windows Server.</summary>
    public string PasswordLabel      => T(IsLinuxBase ? "CVM_UserPwd" : _detectedOsFamily == OsFamily.Client ? "CVM_ClientPwd" : "CVM_AdminPwd");
    public string PreviewTitle       => T(IsLinuxBase ? "CVM_CloudInitPreview" : "CVM_XmlPreview");

    /// <summary>Base selected in the form (firmware settings of Linux bases made from a VM).</summary>
    private BaseVhdx? _base;

    private OsFamily _detectedOsFamily = OsFamily.Server;

    protected override void OnSelectedBaseVhdxChangedCore(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var vhdx = _base = BaseVhdx.FromFile(value);
        IsLinuxBase = vhdx.IsLinux;
        // A base made from a VM only boots with that VM's generation
        if (vhdx.Generation is { } generation) Generation = generation;
        if (vhdx.IsLinux) { UpdateAnswerPreview(); return; }

        // Detect OS family from OsIdentifier
        var norm = vhdx.OsIdentifier.ToLowerInvariant();
        _detectedOsFamily = norm.Contains("server") ? OsFamily.Server : OsFamily.Client;
        OnPropertyChanged(nameof(PasswordLabel));

        // Auto-suggest matching KMS key (keys added in the settings first)
        RefreshKmsKeys();
        var suggested = KmsKeyCatalog.SuggestForOs(vhdx.OsIdentifier);
        if (suggested is not null)
            SelectedKmsKey = suggested;

        // Always refresh unattend preview so client/server profile is applied immediately
        UpdateAnswerPreview();
    }

    /// <summary>The keys of the settings, then the GVLK catalog; refreshed on each visit (a key may have been added meanwhile).</summary>
    [ObservableProperty] private IReadOnlyList<KmsKeyEntry> kmsKeys = KmsKeyCatalog.WithCustom();

    private void RefreshKmsKeys()
    {
        var keys = KmsKeyCatalog.WithCustom();
        if (keys.SequenceEqual(KmsKeys)) return;
        var selected = SelectedKmsKey;   // the ComboBox clears it when its items change
        KmsKeys = keys;
        SelectedKmsKey = selected is not null && keys.Contains(selected) ? selected : null;
    }

    protected override void OnDataLoadedCore() => RefreshKmsKeys();

    public IReadOnlyList<string> Languages     => LabCatalog.Languages;
    public IReadOnlyList<string> TimeZones     => LabCatalog.TimeZones;

    // ─── Hardware options ─────────────────────────────────────────────────────

    public IReadOnlyList<int>    Generations   => LabCatalog.Generations;
    public IReadOnlyList<long>   MemoryOptions => LabCatalog.MemoryOptions;
    public IReadOnlyList<int>    CpuOptions    => LabCatalog.CpuOptions;

    public CreateVmViewModel(IHyperVService hvService, IVhdxService vhdxService)
        : base(hvService, vhdxService)
    {
        vmFolder       = AppSettings.Current.VmsFolder;
        baseVhdxFolder = AppSettings.Current.BaseImagesFolder;
        // Pre-seed computer name from VM name when it changes
        UpdateAnswerPreview();
    }

    protected override void OnVmNameChangedCore(string value)
    {
        if (string.IsNullOrWhiteSpace(ComputerName) || ComputerName == "LAB-VM"
            || ComputerName == InputValidator.ToComputerName(_previousVmName))
            ComputerName = string.IsNullOrWhiteSpace(value) ? "LAB-VM" : InputValidator.ToComputerName(value);
        _previousVmName = value;
    }

    private string _previousVmName = "";

    // Fields that feed the answer file: any change regenerates the preview (debounced)
    private static readonly HashSet<string> AnswerFields =
    [
        nameof(UseAnswerFile), nameof(VmName), nameof(ComputerName), nameof(AdminPassword),
        nameof(AutoLogon), nameof(ProductKey), nameof(UiLanguage), nameof(TimeZone),
        nameof(BuildCbsCache), nameof(DisableWindowsUpdate),
        nameof(LinuxUser), nameof(SshPublicKey), nameof(LinuxCommands), nameof(UseStaticIp), nameof(IpAddress),
        nameof(PrefixLength), nameof(Gateway), nameof(DnsServers),
        nameof(JoinDomainEnabled), nameof(DomainName), nameof(DomainUser), nameof(DomainPassword), nameof(DomainOu),
    ];

    private CancellationTokenSource? _previewCts;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } name && AnswerFields.Contains(name))
            _ = SchedulePreviewAsync();
    }

    private async Task SchedulePreviewAsync()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        try { await Task.Delay(300, cts.Token); }
        catch (OperationCanceledException) { return; }
        UpdateAnswerPreview();
    }

    [RelayCommand]
    public void UpdateAnswerPreview()
    {
        if (!UseAnswerFile) { AnswerFilePreview = T("St_AnswerDisabled"); return; }
        if (IsLinuxBase)
        {
            // First VM of the series, password masked unless shown
            var ip = !UseStaticIp ? null : InputValidator.ParseIpv4(IpAddress) is not null ? IpFor(0) : IpAddress;
            var linux = BuildCloudInit(BulkMode ? InputValidator.ToComputerName($"{BulkPrefix}-01") : ComputerName, ip);
            if (!ShowPassword && !string.IsNullOrEmpty(linux.Password)) linux.Password = "********";
            AnswerFilePreview = CloudInitGenerator.Preview(CloudInitGenerator.Generate(linux));
            return;
        }
        // First VM of the series; its MAC is only known at creation
        var winIp  = !UseStaticIp ? null : InputValidator.ParseIpv4(IpAddress) is not null ? IpFor(0) : IpAddress;
        var config = BuildConfig(null, winIp, winIp is null ? "" : "00-15-5D-XX-XX-XX");
        // Never show the passwords in the preview unless the user asked to see them
        if (!ShowPassword && !string.IsNullOrEmpty(config.AdminPassword)) config.AdminPassword = "********";
        if (!ShowPassword && !string.IsNullOrEmpty(config.DomainPassword)) config.DomainPassword = "********";
        AnswerFilePreview = AnswerFileGenerator.Generate(config);
    }

    /// <summary>Answer-file checks (VM names are validated by the base class); null when valid.</summary>
    private string? ValidateAnswerFile(int vmCount)
    {
        if (!UseAnswerFile) return null;
        if (string.IsNullOrEmpty(AdminPassword)) return T("Val_PasswordEmpty");
        if (!BulkMode && InputValidator.CheckComputerName(ComputerName) is { } c) return T(c);
        if (UseStaticIp)
        {
            if (InputValidator.CheckStaticIpPlan(IpAddress, PrefixLength, Gateway, vmCount) is { } ip) return T(ip);
            if (InputValidator.ParseDnsServers(DnsServers) is null) return T("Val_Dns");
        }
        if (IsLinuxBase)
        {
            if (InputValidator.CheckLinuxUser(LinuxUser) is { } u)       return T(u);
            if (InputValidator.CheckSshPublicKey(SshPublicKey) is { } k) return T(k);
            if (InputValidator.CheckLinuxCommands(CloudInitGenerator.ParseCommands(LinuxCommands)) is { } r) return T(r);
            return null;
        }
        if (InputValidator.CheckProductKey(ProductKey) is { } p) return T(p);
        if (JoinDomainEnabled)
        {
            if (InputValidator.CheckDomainName(DomainName) is { } d) return T(d);
            if (string.IsNullOrWhiteSpace(DomainUser) || string.IsNullOrEmpty(DomainPassword)) return T("Val_DomainCredentials");
        }
        return null;
    }

    /// <summary>Static IP of the clone at <paramref name="index"/> in the series (validated first address + index).</summary>
    private string IpFor(int index) => InputValidator.FormatIpv4(InputValidator.ParseIpv4(IpAddress)!.Value + (uint)index);

    private CloudInitConfig BuildCloudInit(string hostName, string? ip) => new()
    {
        HostName     = hostName,
        UserName     = LinuxUser,
        Password     = AdminPassword,
        SshPublicKey = SshPublicKey,
        RunCommands  = CloudInitGenerator.ParseCommands(LinuxCommands),
        TimeZone     = TimeZone,
        Language     = UiLanguage,
        StaticIp     = ip is null ? null
            : new StaticIpConfig(ip, PrefixLength, Gateway.Trim(), InputValidator.ParseDnsServers(DnsServers) ?? []),
    };

    [RelayCommand]
    public async Task CreateVmAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBaseVhdx) || !File.Exists(SelectedBaseVhdx))
            { Fail(T("St_SelectBaseVhdx")); return; }
        if (string.IsNullOrWhiteSpace(SelectedSwitch))
            { Fail(T("St_SelectSwitch")); return; }
        if (GetNamesToCreate() is not { } names) return;
        if (ValidateAnswerFile(names.Count) is { } err) { Fail(err); return; }

        string parent = SelectedBaseVhdx, sw = SelectedSwitch;
        var linuxBase = IsLinuxBase ? _base ?? BaseVhdx.FromFile(parent) : null;
        var order     = names.ToList();
        await RunCreationAsync(names,
            (name, progress, ct) =>
            {
                if (linuxBase is not null)
                {
                    var ip   = UseAnswerFile && UseStaticIp ? IpFor(order.IndexOf(name)) : null;
                    var seed = UseAnswerFile
                        ? CloudInitGenerator.Generate(BuildCloudInit(BulkMode ? InputValidator.ToComputerName(name) : ComputerName, ip))
                        : null;
                    var linuxNotes = UseAnswerFile
                        ? VmNotes.Build(DateTime.Now, LinuxUser, AdminPassword, ip)
                        : VmNotes.Build(DateTime.Now);
                    // The clone keeps the firmware of the VM the base was made from (Secure Boot template UEFI CA)
                    return HvService.CreateVMWithDifferencingDiskAsync(
                        name, parent, sw, MemoryMB, CpuCount, Generation, VmFolder,
                        null, linuxBase.SecureBoot, pxeBoot: false, linuxNotes,
                        linuxBase.SecureBootTemplate, seed, progress, ct);
                }

                // Static IP on Windows: the adapter gets a static MAC, which identifies it in unattend.xml
                var winIp = UseAnswerFile && UseStaticIp ? IpFor(order.IndexOf(name)) : null;
                var mac   = winIp is null ? null : InputValidator.NewHyperVMac();
                string? answerXml = UseAnswerFile
                    ? AnswerFileGenerator.Generate(BuildConfig(BulkMode ? InputValidator.ToComputerName(name) : null, winIp, mac ?? ""))
                    : null;
                var notes = UseAnswerFile
                    ? VmNotes.Build(DateTime.Now, AnswerFileGenerator.AccountFor(_detectedOsFamily), AdminPassword, winIp)
                    : VmNotes.Build(DateTime.Now);
                return HvService.CreateVMWithDifferencingDiskAsync(
                    name, parent, sw, MemoryMB, CpuCount, Generation, VmFolder,
                    answerXml, secureBoot: true, pxeBoot: false, notes, progress: progress, ct: ct, staticMac: mac);
            },
            onSingleSuccess: () => { VmName = ""; ComputerName = "LAB-VM"; });
    }

    private AnswerFileConfig BuildConfig(string? computerNameOverride = null, string? ip = null, string mac = "") => new()
    {
        StaticIp       = ip is null ? null
            : new StaticIpConfig(ip, PrefixLength, Gateway.Trim(), InputValidator.ParseDnsServers(DnsServers) ?? []),
        MacAddress     = mac,
        JoinDomain     = JoinDomainEnabled ? DomainName.Trim() : "",
        DomainUser     = DomainUser.Trim(),
        DomainPassword = DomainPassword,
        DomainOu       = DomainOu.Trim(),
        ComputerName  = computerNameOverride ?? (string.IsNullOrWhiteSpace(ComputerName) ? VmName : ComputerName),
        AdminPassword = AdminPassword,
        AutoLogon     = AutoLogon,
        ProductKey    = ProductKey,
        UILanguage    = UiLanguage,
        InputLocale   = AnswerFileGenerator.GetInputLocale(UiLanguage),
        SystemLocale  = UiLanguage,
        UserLocale    = UiLanguage,
        TimeZone      = TimeZone,
        ImageIndex    = 1,
        BuildCbsCache = BuildCbsCache,
        DisableWindowsUpdate = DisableWindowsUpdate,
        OsFamily      = _detectedOsFamily,
    };
}

