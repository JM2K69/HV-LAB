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
            // One powershell.exe per query: run them concurrently
            var swTask      = _hvService.GetVirtualSwitchesAsync();
            var natTask     = _natService.GetNatNetworksAsync();
            var adapterTask = _hvService.GetNetworkAdaptersAsync();
            await Task.WhenAll(swTask, natTask, adapterTask);

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
