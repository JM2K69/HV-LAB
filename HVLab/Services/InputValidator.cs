using System.Text.RegularExpressions;

namespace HVLab.Services;

/// <summary>
/// Validation of user input before it reaches a PowerShell script or an answer file.
/// Each method returns a localization key describing the problem, or null when the value is valid.
/// </summary>
public static partial class InputValidator
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    [GeneratedRegex("^[A-Za-z0-9-]{1,15}$")]
    private static partial Regex ComputerNameRegex();

    [GeneratedRegex("^([A-Za-z0-9]{5}-){4}[A-Za-z0-9]{5}$")]
    private static partial Regex ProductKeyRegex();

    /// <summary>
    /// VM names become folder and file names (<c>VmsFolder\Name\Virtual Hard Disks\Name.vhdx</c>):
    /// no path separators, no "..", no invalid file-name characters, no trailing dot/space.
    /// </summary>
    public static string? CheckVmName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))                 return "Val_VmNameEmpty";
        if (name.Length > 100)                               return "Val_VmNameTooLong";
        if (name != name.Trim() || name.EndsWith('.'))       return "Val_VmNameEdges";
        if (name.Contains("..") || name.IndexOfAny(InvalidNameChars) >= 0) return "Val_VmNameChars";
        return null;
    }

    /// <summary>NetBIOS-compatible computer name: 1–15 letters, digits or hyphens, not only digits.</summary>
    public static string? CheckComputerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ComputerNameRegex().IsMatch(name)
            || name.All(char.IsDigit) || name.StartsWith('-') || name.EndsWith('-'))
            return "Val_ComputerName";
        return null;
    }

    /// <summary>Empty (no key) or the 5×5 product key format.</summary>
    public static string? CheckProductKey(string? key)
        => string.IsNullOrWhiteSpace(key) || ProductKeyRegex().IsMatch(key.Trim()) ? null : "Val_ProductKey";

    /// <summary>Derives a valid computer name from a VM name (invalid characters → '-', max 15).</summary>
    public static string ToComputerName(string vmName)
    {
        var chars = vmName.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray();
        var name  = new string(chars).Trim('-');
        if (name.Length > 15) name = name[..15].TrimEnd('-');
        if (name.Length == 0 || name.All(char.IsDigit)) name = "VM-" + name;
        return name.Length > 15 ? name[..15] : name;
    }

    /// <summary>Throws when <paramref name="name"/> is not a valid VM name (defense in depth for the services).</summary>
    public static void EnsureVmName(string name)
    {
        if (CheckVmName(name) is { } key)
            throw new ArgumentException(LocalizationService.Instance[key], nameof(name));
    }
}
