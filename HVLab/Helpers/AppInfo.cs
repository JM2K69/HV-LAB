using System.Reflection;

namespace HVLab.Helpers;

/// <summary>Application metadata read from the assembly (single source: &lt;Version&gt; in HVLab.csproj).</summary>
public static class AppInfo
{
    /// <summary>Display version, e.g. "v1.0.0".</summary>
    public static string Version { get; } =
        "v" + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0");
}
