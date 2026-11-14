using System.Runtime.CompilerServices;
using HVLab.Services;

namespace HVLab.Core.Tests;

internal static class TestSetup
{
    /// <summary>The tests drive the real services: keep them out of the user's %LocalAppData%\HV-LAB\logs.</summary>
    [ModuleInitializer]
    internal static void DisableLogging() => CrashLogger.Enabled = false;
}
