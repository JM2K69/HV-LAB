using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class VirtualSwitchesViewModel(INetworkService hvService, INatService natService, IDialogService dialogs)
    : PageViewModelBase
{
    private readonly INetworkService _hvService = hvService;
    private readonly INatService    _natService = natService;
    private readonly IDialogService _dialogs    = dialogs;

    [ObservableProperty] private ObservableCollection<VirtualSwitch>    virtualSwitches    = [];
    [ObservableProperty] private ObservableCollection<NatNetwork>       natNetworks        = [];
    [ObservableProperty] private ObservableCollection<NetworkAdapterInfo> networkAdapters  = [];
    [ObservableProperty] private ObservableCollection<string>           internalSwitchNames = [];

    public bool IsSwitchListEmpty => IsEmptyAfterLoad(VirtualSwitches.Count);
    public bool IsNatListEmpty    => IsEmptyAfterLoad(NatNetworks.Count);

    protected override void OnEmptyStateChanged()
    {
        OnPropertyChanged(nameof(IsSwitchListEmpty));
        OnPropertyChanged(nameof(IsNatListEmpty));
    }

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = LocalizationService.Instance["St_Ready"];

    // New vSwitch form
    [ObservableProperty] private string              newSwitchName    = "";
    [ObservableProperty] private string              newSwitchType    = "Internal";
    [ObservableProperty] private NetworkAdapterInfo? selectedNetAdapter;
    [ObservableProperty] private bool                enableVlan       = false;
    [ObservableProperty] private int                 newVlanId        = 1;

    // New NAT form
    [ObservableProperty] private string  newNatName       = "";
    [ObservableProperty] private string? selectedNatSwitch;
    [ObservableProperty] private string  natGatewayIP     = "192.168.100.1";
    [ObservableProperty] private int     natPrefixLength  = 24;

    public List<string> SwitchTypes       { get; } = ["External", "Internal", "Private"];
    public bool         IsExternalSwitch  => NewSwitchType == "External";
    public bool         IsNotPrivateSwitch => NewSwitchType != "Private";

    public bool IsVlanVisible => EnableVlan && (NewSwitchType == "External" || NewSwitchType == "Internal");

    partial void OnEnableVlanChanged(bool value)  => OnPropertyChanged(nameof(IsVlanVisible));
    partial void OnNewSwitchTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsExternalSwitch));
        OnPropertyChanged(nameof(IsNotPrivateSwitch));
        OnPropertyChanged(nameof(IsVlanVisible));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Status = LocalizationService.Instance["St_Loading"];
        try
        {
            // WMI queries, run concurrently
            var swTask      = _hvService.GetVirtualSwitchesAsync();
            var natTask     = _natService.GetNatNetworksAsync();
            var adapterTask = _hvService.GetNetworkAdaptersAsync();
            var vmNicTask   = _hvService.GetVmNetworkAdaptersAsync();
            var hostTask    = _hvService.GetHostAddressesAsync();
            await Task.WhenAll(swTask, natTask, adapterTask, vmNicTask, hostTask);

            var switches = swTask.Result;
            VirtualSwitches.Clear();
            InternalSwitchNames.Clear();
            var hostSwitch = NewHostAdapterSwitch;
            HostSwitchNames.Clear();
            foreach (var sw in switches)
            {
                VirtualSwitches.Add(sw);
                if (sw.SwitchType == "Internal") InternalSwitchNames.Add(sw.Name);
                if (sw.SwitchType != "Private") HostSwitchNames.Add(sw.Name);
            }
            NewHostAdapterSwitch = hostSwitch is not null && HostSwitchNames.Contains(hostSwitch) ? hostSwitch : null;

            var nats = natTask.Result;
            NatNetworks.Clear();
            foreach (var nat in nats) NatNetworks.Add(nat);

            var adapters = adapterTask.Result;
            NetworkAdapters.Clear();
            foreach (var a in adapters) NetworkAdapters.Add(a);

            Topology.Clear();
            foreach (var t in TopologyBuilder.Build(switches, nats, vmNicTask.Result, hostTask.Result)) Topology.Add(t);

            Status = string.Format(LocalizationService.Instance["SW_Summary"], switches.Count, nats.Count);
            MarkLoaded();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); }
        finally { IsLoading = false; }
        if (HostAdaptersLoaded) await LoadHostAdaptersAsync();   // the host cards section is open: kept current
    }

    // ─── Host network cards (vEthernet): one per VLAN, as Add-VMNetworkAdapter -ManagementOS ──

    /// <summary>Read through PowerShell when their section opens, then with each refresh.</summary>
    public ObservableCollection<HostAdapter> HostAdapters { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoHostAdapters))] private bool hostAdaptersLoaded;
    public bool HasNoHostAdapters => HostAdaptersLoaded && HostAdapters.Count == 0;

    /// <summary>Switches a host card can be plugged into: External and Internal (a Private switch has none).</summary>
    public ObservableCollection<string> HostSwitchNames { get; } = [];
    [ObservableProperty] private string? newHostAdapterSwitch;
    [ObservableProperty] private string  newHostAdapterName = "";
    /// <summary>0 = untagged.</summary>
    [ObservableProperty] private double  newHostAdapterVlan;
    [ObservableProperty] private bool    newHostAdapterNoDhcp;

    public async Task LoadHostAdaptersAsync()
    {
        try
        {
            var cards = await _hvService.GetHostAdaptersAsync();
            HostAdapters.Clear();
            foreach (var c in cards) HostAdapters.Add(c);
            HostAdaptersLoaded = true;
            OnPropertyChanged(nameof(HasNoHostAdapters));
        }
        catch (Exception ex) { ShowActionError(ex); }
    }

    [RelayCommand]
    private async Task AddHostAdapterAsync()
    {
        var name = NewHostAdapterName.Trim();
        if (InputValidator.CheckHostAdapterName(name) is { } key) { ShowActionError(T(key)); return; }
        if (VirtualSwitches.FirstOrDefault(s => s.Name == NewHostAdapterSwitch && s.SwitchType != "Private") is not { } sw)
        { ShowActionError(T("HOSTNIC_ErrNoSwitch")); return; }
        var vlan = double.IsNaN(NewHostAdapterVlan) ? 0 : (int)NewHostAdapterVlan;
        if (vlan is < 0 or > 4094) { ShowActionError(T("HOSTNIC_ErrVlan")); return; }
        if (await RunHostCardAsync(T("HOSTNIC_Adding", name),
                () => _hvService.AddHostAdapterAsync(sw.Target, name, vlan, !NewHostAdapterNoDhcp)))
            NewHostAdapterName = "";
    }

    [RelayCommand]
    private async Task ChangeHostAdapterVlanAsync(HostAdapter card)
    {
        var answer = await _dialogs.PromptAsync(T("HOSTNIC_VlanTitle", card.Name), T("HOSTNIC_VlanLabel"),
                                                card.VlanId > 0 ? card.VlanId.ToString() : "", allowEmpty: true);
        if (answer is null) return;
        var text = answer.Trim();
        if (!int.TryParse(text.Length == 0 ? "0" : text, out var vlan) || vlan is < 0 or > 4094)
        { ShowActionError(T("HOSTNIC_ErrVlan")); return; }
        if (vlan == card.VlanId && !card.VlanMode.Equals("Trunk", StringComparison.OrdinalIgnoreCase)) return;
        await RunHostCardAsync(T("HOSTNIC_ChangingVlan", card.Name), () => _hvService.SetHostAdapterVlanAsync(card.Id, vlan));
    }

    [RelayCommand]
    private Task ToggleHostAdapterDhcpAsync(HostAdapter card)
        => RunHostCardAsync(T("HOSTNIC_ChangingDhcp", card.Name), () => _hvService.SetHostAdapterDhcpAsync(card.Id, !card.Dhcp));

    [RelayCommand]
    private async Task RemoveHostAdapterAsync(HostAdapter card)
    {
        if (!await _dialogs.ConfirmAsync(T("HOSTNIC_DeleteTitle"), T("HOSTNIC_DeleteText", card.Name, card.SwitchName), T("Btn_Delete")))
            return;
        await RunHostCardAsync(T("HOSTNIC_Removing", card.Name), () => _hvService.RemoveHostAdapterAsync(card.Id));
    }

    /// <summary>A host card change, then the switches (their VLAN column) and the cards read again; true when it worked.</summary>
    private async Task<bool> RunHostCardAsync(string status, Func<Task> change)
    {
        ClearActionError();
        IsLoading = true;
        Status = status;
        var ok = false;
        try { await change(); ok = true; }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); }
        finally { IsLoading = false; }
        HostAdaptersLoaded = true;
        await RefreshAsync();
        return ok;
    }

    [RelayCommand]
    public async Task CreateSwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSwitchName)) { ShowActionError(LocalizationService.Instance["SW_ErrNoName"]); return; }
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_CreatingSwitch"], NewSwitchName);
        try
        {
            switch (NewSwitchType)
            {
                case "External":
                    if (SelectedNetAdapter is null)
                        throw new InvalidOperationException(LocalizationService.Instance["SW_ErrNoAdapter"]);
                    await _hvService.CreateExternalSwitchAsync(NewSwitchName, SelectedNetAdapter.Name, EnableVlan ? NewVlanId : 0);
                    break;
                case "Internal": await _hvService.CreateInternalSwitchAsync(NewSwitchName, EnableVlan ? NewVlanId : 0); break;
                default:         await _hvService.CreatePrivateSwitchAsync(NewSwitchName);  break;
            }
            NewSwitchName = "";
            Status = LocalizationService.Instance["SW_CreatedSwitch"];
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }

    [RelayCommand]
    public async Task RemoveSwitchAsync(VirtualSwitch sw)
    {
        if (!await _dialogs.ConfirmAsync(LocalizationService.Instance["SW_DeleteTitle"],
                $"{LocalizationService.Instance["SW_DeleteConfirm"]} « {sw.Name} » ?",
                LocalizationService.Instance["Btn_Delete"]))
            return;
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_DeletingSwitch"], sw.Name);
        try
        {
            await _hvService.RemoveVSwitchAsync(sw.Target);   // by GUID: never a namesake
            Status = string.Format(LocalizationService.Instance["SW_DeletedSwitch"], sw.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateNatAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNatName) || string.IsNullOrWhiteSpace(SelectedNatSwitch))
        { ShowActionError(LocalizationService.Instance["SW_ErrNoNat"]); return; }
        if (NatService.NormalizeGateway(NatGatewayIP, NatPrefixLength) is null)
        { ShowActionError(LocalizationService.Instance["SW_ErrInvalidGateway"]); return; }
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_CreatingNat"], NewNatName);
        try
        {
            await _natService.CreateNatNetworkAsync(SelectedNatSwitch, NewNatName, NatGatewayIP, NatPrefixLength);
            NewNatName = "";
            Status = LocalizationService.Instance["SW_CreatedNat"];
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }

    // ─── Topology view ──────────────────────────────────────────────────────

    /// <summary>Each switch with its uplink and the VM adapters plugged into it (see <see cref="TopologyBuilder"/>).</summary>
    public ObservableCollection<TopologySwitch> Topology { get; } = [];

    // ─── Change the type of a switch (dialog shown by the view) ─────────────

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChangeTypeWarning), nameof(HasChangeTypeWarning))]
    private VirtualSwitch? editingSwitch;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditIsExternal), nameof(ChangeTypeWarning), nameof(HasChangeTypeWarning))]
    private string editSwitchType = "Internal";
    [ObservableProperty] private NetworkAdapterInfo? editSwitchAdapter;

    public bool EditIsExternal => EditSwitchType == "External";

    /// <summary>What the change breaks or interrupts: a NAT on the switch, the host's network for an External switch.</summary>
    public string ChangeTypeWarning
    {
        get
        {
            if (EditingSwitch is not { } sw || sw.SwitchType == EditSwitchType) return "";
            var nat = NatNetworks.FirstOrDefault(n => n.SwitchName == sw.Name);
            var parts = new List<string>();
            if (nat is not null && EditSwitchType != "Internal") parts.Add(T("SW_TypeNatWarning", nat.Name));
            if (EditSwitchType == "External")  parts.Add(T("SW_TypeExternalWarning"));
            if (sw.SwitchType == "External")   parts.Add(T("SW_TypeLeaveExternal"));
            return string.Join(" ", parts);
        }
    }
    public bool HasChangeTypeWarning => ChangeTypeWarning.Length > 0;

    public void BeginChangeType(VirtualSwitch sw)
    {
        EditingSwitch     = sw;
        EditSwitchType    = sw.SwitchType;
        EditSwitchAdapter = NetworkAdapters.FirstOrDefault(a => a.Name == sw.NetAdapterName);
    }

    /// <summary>Validation message of the change, or null when it can be applied.</summary>
    public string? ValidateChangeType()
    {
        if (EditingSwitch is not { } sw) return null;
        if (EditIsExternal && EditSwitchAdapter is null) return T("SW_ErrNoAdapter");
        if (EditSwitchType == sw.SwitchType && (!EditIsExternal || EditSwitchAdapter?.Name == sw.NetAdapterName))
            return T("SW_TypeUnchanged");
        return null;
    }

    public async Task ApplyChangeTypeAsync()
    {
        if (EditingSwitch is not { } sw) return;
        IsLoading = true;
        Status = T("SW_ChangingType", sw.Name);
        try
        {
            await _hvService.ChangeSwitchTypeAsync(sw.Target, EditSwitchType, EditIsExternal ? EditSwitchAdapter?.Name : null);
            Status = T("SW_TypeChanged", sw.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }

    // ─── NAT with DHCP: Windows Internet connection sharing ─────────────────

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IcsStatusText), nameof(IsIcsOn))] private IcsStatus? icsStatus;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IcsStatusText))] private bool icsLoaded;
    [ObservableProperty] private string? icsSwitch;
    [ObservableProperty] private NetworkAdapterInfo? icsPublicAdapter;

    public bool IsIcsOn => IcsStatus is not null;
    public string IcsStatusText => !IcsLoaded ? T("ICS_Loading")
        : IcsStatus is { } s ? T("ICS_On", s.PublicConnection, s.PrivateConnection) : T("ICS_Off");

    /// <summary>Reads the current sharing (PowerShell COM call, so only when the section is opened).</summary>
    [RelayCommand]
    public async Task LoadIcsAsync()
    {
        try { IcsStatus = await _hvService.GetIcsStatusAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { IcsLoaded = true; }
    }

    [RelayCommand]
    public async Task EnableIcsAsync()
    {
        if (string.IsNullOrWhiteSpace(IcsSwitch) || IcsPublicAdapter is null) { ShowActionError(T("ICS_ErrSelect")); return; }
        if (NatNetworks.FirstOrDefault(n => n.SwitchName == IcsSwitch) is { } nat) { ShowActionError(T("ICS_ErrNatOnSwitch", nat.Name)); return; }
        if (!await _dialogs.ConfirmAsync(T("ICS_ConfirmTitle"), T("ICS_ConfirmEnable", IcsPublicAdapter.Name, IcsSwitch), T("ICS_Enable")))
            return;
        await RunIcsAsync(T("ICS_Enabling"), () => _hvService.EnableIcsAsync(IcsPublicAdapter.Name, IcsSwitch));
    }

    [RelayCommand]
    public async Task DisableIcsAsync()
    {
        if (!await _dialogs.ConfirmAsync(T("ICS_ConfirmTitle"), T("ICS_ConfirmDisable"), T("ICS_Disable"))) return;
        await RunIcsAsync(T("ICS_Disabling"), _hvService.DisableIcsAsync);
    }

    private async Task RunIcsAsync(string status, Func<Task> action)
    {
        IsLoading = true;
        Status = status;
        try
        {
            await action();
            await LoadIcsAsync();
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }

    private static string T(string key, params object?[] args) => LocalizationService.T(key, args);

    [RelayCommand]
    public async Task RemoveNatAsync(NatNetwork nat)
    {
        if (!await _dialogs.ConfirmAsync(LocalizationService.Instance["NAT_DeleteTitle"],
                $"{LocalizationService.Instance["NAT_DeleteConfirm"]} « {nat.Name} » ?",
                LocalizationService.Instance["Btn_Delete"]))
            return;
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_DeletingNat"], nat.Name);
        try
        {
            await _natService.RemoveNatNetworkAsync(nat.Name);
            Status = string.Format(LocalizationService.Instance["SW_DeletedNat"], nat.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowActionError(ex); IsLoading = false; }
    }
}
