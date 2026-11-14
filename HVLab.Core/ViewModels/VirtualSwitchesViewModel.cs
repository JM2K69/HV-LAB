using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class VirtualSwitchesViewModel(IHyperVService hvService, INatService natService, IDialogService dialogs)
    : PageViewModelBase
{
    private readonly IHyperVService _hvService  = hvService;
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
            foreach (var sw in switches)
            {
                VirtualSwitches.Add(sw);
                if (sw.SwitchType == "Internal") InternalSwitchNames.Add(sw.Name);
            }

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
    }

    [RelayCommand]
    public async Task CreateSwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSwitchName)) { Status = LocalizationService.Instance["SW_ErrNoName"]; return; }
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
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
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
            await _hvService.RemoveVSwitchAsync(sw.Name);
            Status = string.Format(LocalizationService.Instance["SW_DeletedSwitch"], sw.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateNatAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNatName) || string.IsNullOrWhiteSpace(SelectedNatSwitch))
        { Status = LocalizationService.Instance["SW_ErrNoNat"]; return; }
        if (NatService.NormalizeGateway(NatGatewayIP, NatPrefixLength) is null)
        { Status = LocalizationService.Instance["SW_ErrInvalidGateway"]; return; }
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_CreatingNat"], NewNatName);
        try
        {
            await _natService.CreateNatNetworkAsync(SelectedNatSwitch, NewNatName, NatGatewayIP, NatPrefixLength);
            NewNatName = "";
            Status = LocalizationService.Instance["SW_CreatedNat"];
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
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
            await _hvService.ChangeSwitchTypeAsync(sw.Name, EditSwitchType, EditIsExternal ? EditSwitchAdapter?.Name : null);
            Status = T("SW_TypeChanged", sw.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
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
        if (string.IsNullOrWhiteSpace(IcsSwitch) || IcsPublicAdapter is null) { Status = T("ICS_ErrSelect"); return; }
        if (NatNetworks.FirstOrDefault(n => n.SwitchName == IcsSwitch) is { } nat) { Status = T("ICS_ErrNatOnSwitch", nat.Name); return; }
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
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
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
        catch (Exception ex) { Status = string.Empty; ShowError(ex); IsLoading = false; }
    }
}
