using System.Reflection;

namespace HVLab.Helpers;

/// <summary>Application metadata read from the assembly (single source: VersionPrefix in Directory.Build.props).</summary>
public static class AppInfo
{
    /// <summary>Display version, e.g. "v1.0.0" or "v1.1.0-beta.1" (informational version without the commit hash).</summary>
    public static string Version { get; } = "v" + ReadVersion();

    private static string ReadVersion()
    {
        var asm  = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
            return info.Split('+')[0];   // "1.1.0-beta.1+<sha>" -> "1.1.0-beta.1"
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
