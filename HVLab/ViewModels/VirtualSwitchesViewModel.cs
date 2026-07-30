using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

public partial class VirtualSwitchesViewModel : ObservableObject
{
    private readonly HyperVService _hvService = new();
    private readonly NatService _natService = new();

    [ObservableProperty] private ObservableCollection<VirtualSwitch>    virtualSwitches    = [];
    [ObservableProperty] private ObservableCollection<NatNetwork>       natNetworks        = [];
    [ObservableProperty] private ObservableCollection<NetworkAdapterInfo> networkAdapters  = [];
    [ObservableProperty] private ObservableCollection<string>           internalSwitchNames = [];

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = "Prêt";

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
        Status = "Chargement…";
        try
        {
            var switches = await _hvService.GetVirtualSwitchesAsync();
            VirtualSwitches.Clear();
            InternalSwitchNames.Clear();
            foreach (var sw in switches)
            {
                VirtualSwitches.Add(sw);
                if (sw.SwitchType == "Internal") InternalSwitchNames.Add(sw.Name);
            }

            var nats = await _natService.GetNatNetworksAsync();
            NatNetworks.Clear();
            foreach (var nat in nats) NatNetworks.Add(nat);

            var adapters = await _hvService.GetNetworkAdaptersAsync();
            NetworkAdapters.Clear();
            foreach (var a in adapters) NetworkAdapters.Add(a);

            Status = string.Format(LocalizationService.Instance["SW_Summary"], switches.Count, nats.Count);
        }
        catch (Exception ex) { Status = string.Format(LocalizationService.Instance["SW_Error"], ex.Message); }
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
        catch (Exception ex) { Status = string.Format(LocalizationService.Instance["SW_Error"], ex.Message); IsLoading = false; }
    }

    public async Task RemoveSwitchAsync(VirtualSwitch sw)
    {
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_DeletingSwitch"], sw.Name);
        try
        {
            await _hvService.RemoveVSwitchAsync(sw.Name);
            Status = string.Format(LocalizationService.Instance["SW_DeletedSwitch"], sw.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Format(LocalizationService.Instance["SW_Error"], ex.Message); IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateNatAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNatName) || string.IsNullOrWhiteSpace(SelectedNatSwitch))
        { Status = LocalizationService.Instance["SW_ErrNoNat"]; return; }
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_CreatingNat"], NewNatName);
        try
        {
            await _natService.CreateNatNetworkAsync(SelectedNatSwitch, NewNatName, NatGatewayIP, NatPrefixLength);
            NewNatName = "";
            Status = LocalizationService.Instance["SW_CreatedNat"];
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Format(LocalizationService.Instance["SW_Error"], ex.Message); IsLoading = false; }
    }

    public async Task RemoveNatAsync(NatNetwork nat)
    {
        IsLoading = true;
        Status = string.Format(LocalizationService.Instance["SW_DeletingNat"], nat.Name);
        try
        {
            await _natService.RemoveNatNetworkAsync(nat.Name);
            Status = string.Format(LocalizationService.Instance["SW_DeletedNat"], nat.Name);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = string.Format(LocalizationService.Instance["SW_Error"], ex.Message); IsLoading = false; }
    }
}
