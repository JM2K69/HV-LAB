using System.Text.Json;

namespace HVLab.Models;

/// <summary>
/// Sidecar of a base image (<c>BASE_….json</c> next to <c>BASE_….vhdx</c>): what the file name cannot say.
/// Written when a base is made from an existing VM, so its clones get the same firmware settings.
/// Bases built from a WIM have none and keep the Windows defaults.
/// </summary>
public sealed class BaseImageInfo
{
    public const string WindowsSecureBootTemplate = "MicrosoftWindows";
    public const string LinuxSecureBootTemplate   = "MicrosoftUEFICertificateAuthority";

    /// <summary>"Linux" or "Windows".</summary>
    public string  OsFamily           { get; set; } = "Windows";
    public string  Distribution       { get; set; } = "";
    public int     Generation         { get; set; } = 2;
    public bool    SecureBoot         { get; set; } = true;
    public string  SecureBootTemplate { get; set; } = WindowsSecureBootTemplate;
    /// <summary>VM the base was copied from, for information.</summary>
    public string? SourceVm           { get; set; }

    public bool IsLinux => OsFamily.Equals("Linux", StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SidecarPath(string vhdxPath) => Path.ChangeExtension(vhdxPath, ".json");

    /// <summary>Sidecar of <paramref name="vhdxPath"/>, or null when there is none or it cannot be read.</summary>
    public static BaseImageInfo? Load(string vhdxPath)
    {
        try
        {
            var path = SidecarPath(vhdxPath);
            return File.Exists(path) ? JsonSerializer.Deserialize<BaseImageInfo>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(string vhdxPath) => File.WriteAllText(SidecarPath(vhdxPath), JsonSerializer.Serialize(this, Options));
}
