using HVLab.Services;
using Microsoft.UI.Xaml;

namespace HVLab.Helpers;

/// <summary>x:Bind functions on strings.</summary>
public static class Text
{
    public static bool IsNotEmpty(string? value) => !string.IsNullOrEmpty(value);
}

/// <summary>
/// x:Bind functions standing in for converters in Window XAML, where x:Bind cannot look up a
/// StaticResource converter (it needs a FrameworkElement root).
/// </summary>
public static class Display
{
    private static readonly Converters.StateToColorConverter StateColors = new();

    public static Microsoft.UI.Xaml.Media.Brush StateBrush(string state)
        => (Microsoft.UI.Xaml.Media.Brush)StateColors.Convert(state, typeof(Microsoft.UI.Xaml.Media.Brush), null!, "");

    public static Visibility CollapsedWhen(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>x:Bind functions for the checkpoint tree of the VM window.</summary>
public static class CheckpointDisplay
{
    /// <summary>Indent of a tree row.</summary>
    public static Thickness Indent(double left) => new(left, 0, 0, 0);

    /// <summary>History icon for a checkpoint, play icon for the "Now" marker.</summary>
    public static string Glyph(bool isNow) => isNow ? "" : "";

    public static string Name(string name, bool isNow) => isNow ? LocalizationService.Instance["CP_Now"] : name;
}
