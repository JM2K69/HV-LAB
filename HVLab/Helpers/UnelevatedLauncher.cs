using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HVLab.Services;

namespace HVLab.Helpers;

/// <summary>
/// HV-LAB runs as administrator. SSH, Remote Desktop and Windows Terminal have no reason to: they are started through
/// the desktop shell (Explorer), so they run as the signed-in user, unelevated, and "wt.exe" is resolved in the user's
/// own context, never by the elevated process. This is the documented way: find the desktop's shell view, take its
/// Shell.Application object, and call ShellExecute there.
/// When the shell is not available (no Explorer), the program starts from its absolute System32 path instead.
/// </summary>
internal static class UnelevatedLauncher
{
    /// <summary>Starts <paramref name="file"/> as the desktop user; false when the shell could not do it.</summary>
    public static bool TryStart(string file, string arguments)
    {
        try
        {
            var windows = (IShellWindows)new ShellWindowsClass();
            object location = 0;   // CSIDL_DESKTOP
            object root = Missing.Value;
            var desktop = windows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);

            var service = SID_STopLevelBrowser;
            var iidBrowser = typeof(IShellBrowser).GUID;
            var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(ref service, ref iidBrowser);
            var view = browser.QueryActiveShellView();
            var iidDispatch = IID_IDispatch;
            var folderView = view.GetItemObject(SVGIO_BACKGROUND, ref iidDispatch);

            var shell = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null)!;
            shell.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, shell,
                [file, arguments, "", "open", 1 /* SW_SHOWNORMAL */]);
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Log($"Unelevated start of {file}", ex);
            return false;
        }
    }

    /// <summary>Program of System32 (or a sub-folder): never resolved through PATH by the elevated process.</summary>
    public static string System32(string relative) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), relative);

    /// <summary>Unelevated through the shell, else elevated from an absolute path.</summary>
    public static void Start(string file, string arguments, string fallbackAbsolutePath, string fallbackArguments)
    {
        if (TryStart(file, arguments)) return;
        Process.Start(new ProcessStartInfo(fallbackAbsolutePath, fallbackArguments) { UseShellExecute = false });
    }

    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), ClassInterface(ClassInterfaceType.None)]
    private class ShellWindowsClass;

    // Only FindWindowSW is called: the methods before it keep the vtable order
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object NewEnum();
        void Register();
        void RegisterPending();
        void Revoke(int cookie);
        void OnNavigate();
        void OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    // IOleWindow then IShellBrowser, up to QueryActiveShellView
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(int enterMode);
        void InsertMenusSB(IntPtr menu, IntPtr widths);
        void SetMenuSB(IntPtr menu, IntPtr oleMenu, IntPtr activeObject);
        void RemoveMenusSB(IntPtr menu);
        void SetStatusTextSB(IntPtr text);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(IntPtr msg, ushort id);
        void BrowseObject(IntPtr pidl, uint flags);
        void GetViewStateStream(uint mode, IntPtr stream);
        void GetControlWindow(uint id, out IntPtr hwnd);
        void SendControlMsg(uint id, uint msg, IntPtr wParam, IntPtr lParam, IntPtr result);
        IShellView QueryActiveShellView();
    }

    // IOleWindow then IShellView, up to GetItemObject
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(int enterMode);
        void TranslateAccelerator(IntPtr msg);
        void EnableModeless(int enable);
        void UIActivate(uint state);
        void Refresh();
        void CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr hwnd);
        void DestroyViewWindow();
        void GetCurrentInfo(IntPtr settings);
        void AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam);
        void SaveViewState();
        void SelectItem(IntPtr pidl, uint flags);
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetItemObject(uint item, ref Guid riid);
    }
}
