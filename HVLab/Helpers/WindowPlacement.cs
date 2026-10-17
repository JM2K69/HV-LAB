using System.Runtime.InteropServices;
using HVLab.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace HVLab.Helpers;

/// <summary>
/// DPI-aware window sizing for the main window: minimum size (WM_GETMINMAXINFO),
/// restore of the last size/position/maximized state, and save on close.
/// Sizes are expressed in DIPs and scaled with the window's DPI.
/// </summary>
public static class WindowPlacement
{
    private const int DefaultWidth  = 1200, DefaultHeight = 800;
    private const int MinWidth      = 1000, MinHeight     = 640;

    // Kept alive for the lifetime of the window (native code holds a pointer to it)
    private static SubclassProc? _subclassProc;

    public static void Attach(Window window)
    {
        var hwnd      = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var appWindow = window.AppWindow;

        _subclassProc = (h, msg, wParam, lParam, id, data) =>
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var scale = GetDpiForWindow(h) / 96.0;
                var info  = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize.X = (int)(MinWidth  * scale);
                info.ptMinTrackSize.Y = (int)(MinHeight * scale);
                Marshal.StructureToPtr(info, lParam, false);
            }
            return DefSubclassProc(h, msg, wParam, lParam);
        };
        SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero);

        Restore(appWindow, GetDpiForWindow(hwnd) / 96.0);
        appWindow.Closing += (_, _) => Save(appWindow);
    }

    private static void Restore(AppWindow appWindow, double scale)
    {
        var s = AppSettings.Current;
        if (s.WindowWidth is > 0 && s.WindowHeight is > 0 && s.WindowX is { } x && s.WindowY is { } y)
        {
            var rect = new RectInt32(x, y, s.WindowWidth.Value, s.WindowHeight.Value);
            var area = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
            if (area is not null) // still on a connected monitor
            {
                appWindow.MoveAndResize(rect);
                if (s.WindowMaximized && appWindow.Presenter is OverlappedPresenter p) p.Maximize();
                return;
            }
        }

        // First run (or saved monitor gone): default size, centered on the nearest monitor
        var w    = (int)(DefaultWidth * scale);
        var h    = (int)(DefaultHeight * scale);
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        w = Math.Min(w, work.Width);
        h = Math.Min(h, work.Height);
        appWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    private static void Save(AppWindow appWindow)
    {
        var s = AppSettings.Current;
        var maximized = appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        s.WindowMaximized = maximized;
        if (!maximized && appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            s.WindowX      = appWindow.Position.X;
            s.WindowY      = appWindow.Position.Y;
            s.WindowWidth  = appWindow.Size.Width;
            s.WindowHeight = appWindow.Size.Height;
        }
        try { s.Save(); } catch (Exception ex) { CrashLogger.Log("WindowPlacement.Save", ex); }
    }

    // ── Win32 ───────────────────────────────────────────────────────────────

    private const uint WM_GETMINMAXINFO = 0x0024;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
}
