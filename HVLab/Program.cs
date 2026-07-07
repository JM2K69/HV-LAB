using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace HVLab;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Self-contained mode: the WinUI/WindowsAppRuntime is embedded in the output folder.
        // Bootstrap.Initialize() must NOT be called here — it is only for framework-dependent
        // (non-self-contained) unpackaged apps that rely on a system-installed runtime.
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var ctx = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            _ = NativeMethods.MessageBox(IntPtr.Zero, ex.ToString(), "HV-LAB — Erreur fatale", 0x10);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}

