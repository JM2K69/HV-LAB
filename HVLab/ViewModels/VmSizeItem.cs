using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;

namespace HVLab.ViewModels;

/// <summary>
/// Observable wrapper around a <see cref="VmSizeProfile"/>.
/// Carries its own <see cref="SelectCommand"/> pre-wired by the parent ViewModel,
/// so the DataTemplate needs no reference back to the page or parent ViewModel.
/// </summary>
public partial class VmSizeItem : ObservableObject
{
    public VmSizeProfile Profile { get; init; } = null!;

    /// <summary>Set by <see cref="QuickVmViewModel"/> at construction time.</summary>
    public IRelayCommand SelectCommand { get; init; } = null!;

    [ObservableProperty] private bool isSelected;

    // ── Display properties delegated to the profile ───────────────────────────
    public string Label         => Profile.Label;
    public string AccentColor   => Profile.AccentColor;
    public string CpuDisplay    => Profile.CpuDisplay;
    public string MemoryDisplay => Profile.MemoryDisplay;

    /// <summary>Localized use-case description resolved at read time.</summary>
    public string UseCaseText   => LocalizationService.Instance[Profile.UseCase];
}
