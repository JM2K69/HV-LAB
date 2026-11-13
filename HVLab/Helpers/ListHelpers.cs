using HVLab.Services;
using Microsoft.UI.Xaml;

namespace HVLab.Helpers;

/// <summary>x:Bind functions for the checkpoint tree of the VM detail pane.</summary>
public static class CheckpointDisplay
{
    /// <summary>Indent of a tree row.</summary>
    public static Thickness Indent(double left) => new(left, 0, 0, 0);

    /// <summary>History icon for a checkpoint, play icon for the "Now" marker.</summary>
    public static string Glyph(bool isNow) => isNow ? "" : "";

    public static string Name(string name, bool isNow) => isNow ? LocalizationService.Instance["CP_Now"] : name;
}
