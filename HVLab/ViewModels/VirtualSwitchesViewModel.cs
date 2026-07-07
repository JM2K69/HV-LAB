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

    [ObservableProperty] private ObservableCollection<VirtualSwitch> virtualSwitches = [];
    [ObservableProperty] private ObservableCollection<NatNetwork> natNetworks = [];
    [ObservableProperty] private ObservableCollection<string> networkAdapters = [];
    [ObservableProperty] private ObservableCollection<string> internalSwitchNames = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Prêt";

    // New vSwitch form
    [ObservableProperty] private string newSwitchName = "";
    [ObservableProperty] private string newSwitchType = "Internal";
    [ObservableProperty] private string? selectedNetAdapter;

    // New NAT form
    [ObservableProperty] private string newNatName = "";
    [ObservableProperty] private string? selectedNatSwitch;
    [ObservableProperty] private string natGatewayIP = "192.168.100.1";
    [ObservableProperty] private int natPrefixLength = 24;

    public List<string> SwitchTypes { get; } = ["External", "Internal", "Private"];

    public bool IsExternalSwitch => NewSwitchType == "External";

    partial void OnNewSwitchTypeChanged(string value) => OnPropertyChanged(nameof(IsExternalSwitch));

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

            Status = $"{switches.Count} commutateur(s), {nats.Count} réseau(x) NAT";
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateSwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSwitchName)) { Status = "Saisissez un nom."; return; }
        IsLoading = true;
        Status = $"Création du commutateur '{NewSwitchName}'…";
        try
        {
            switch (NewSwitchType)
            {
                case "External":
                    if (string.IsNullOrEmpty(SelectedNetAdapter))
                        throw new InvalidOperationException("Sélectionnez un adaptateur réseau.");
                    await _hvService.CreateExternalSwitchAsync(NewSwitchName, SelectedNetAdapter);
                    break;
                case "Internal": await _hvService.CreateInternalSwitchAsync(NewSwitchName); break;
                default:         await _hvService.CreatePrivateSwitchAsync(NewSwitchName);  break;
            }
            NewSwitchName = "";
            Status = "Commutateur créé avec succès.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    public async Task RemoveSwitchAsync(VirtualSwitch sw)
    {
        IsLoading = true;
        Status = $"Suppression de '{sw.Name}'…";
        try
        {
            await _hvService.RemoveVSwitchAsync(sw.Name);
            Status = $"Commutateur '{sw.Name}' supprimé.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    [RelayCommand]
    public async Task CreateNatAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNatName) || string.IsNullOrWhiteSpace(SelectedNatSwitch))
        { Status = "Saisissez un nom NAT et sélectionnez un commutateur interne."; return; }
        IsLoading = true;
        Status = $"Création du réseau NAT '{NewNatName}'…";
        try
        {
            await _natService.CreateNatNetworkAsync(SelectedNatSwitch, NewNatName, NatGatewayIP, NatPrefixLength);
            NewNatName = "";
            Status = "Réseau NAT créé avec succès.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }

    public async Task RemoveNatAsync(NatNetwork nat)
    {
        IsLoading = true;
        Status = $"Suppression du NAT '{nat.Name}'…";
        try
        {
            await _natService.RemoveNatNetworkAsync(nat.Name);
            Status = $"NAT '{nat.Name}' supprimé.";
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Erreur : {ex.Message}"; IsLoading = false; }
    }
}
