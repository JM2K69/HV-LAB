using System.Runtime.InteropServices;

namespace HVLab.Helpers;

/// <summary>
/// Opens the native Win32 IFileOpenDialog in folder-pick mode.
/// Works correctly even when the process is running elevated (Administrator),
/// which is required for Hyper-V management but breaks the WinRT FolderPicker.
/// </summary>
public static class Win32FolderPicker
{
    // ── IFileOpenDialog COM GUIDs ────────────────────────────────────────────
    private static readonly Guid CLSID_FileOpenDialog =
        new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");

    private static readonly Guid IID_IFileOpenDialog =
        new("D57C7288-D4AD-4768-BE02-9D969532D960");

    private static readonly Guid IID_IShellItem =
        new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    // FOS flags
    private const uint FOS_PICKFOLDERS      = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM  = 0x00000040;
    private const uint FOS_NOVALIDATE       = 0x00000100;
    private const uint FOS_PATHMUSTEXIST    = 0x00000800;
    private const uint FOS_FILEMUSTEXIST    = 0x00001000;

    private const uint SIGDN_FILESYSPATH    = 0x80058000;
    private const int  S_OK                 = 0;
    private const int  CANCELLED            = unchecked((int)0x800704C7);

    // ── COM interfaces (minimal surface) ────────────────────────────────────

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig] int Show(IntPtr hwndOwner);

        // IFileDialog
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IntPtr psi);
        void SetFolder(IntPtr psi);
        void GetFolder(out IntPtr ppsi);
        void GetCurrentSelection(out IntPtr ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IntPtr psi, uint fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);

        // IFileOpenDialog
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext,
        ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a folder-browser dialog owned by <paramref name="hwnd"/>.
    /// Returns the selected path, or <c>null</c> if the user cancelled.
    /// Safe to call from an elevated (Administrator) process.
    /// </summary>
    public static string? Pick(IntPtr hwnd, string? title = null)
    {
        var clsid = CLSID_FileOpenDialog;
        var iid   = IID_IFileOpenDialog;
        int hr    = CoCreateInstance(ref clsid, IntPtr.Zero, 1 /*CLSCTX_INPROC_SERVER*/,
                                     ref iid, out object obj);
        if (hr != S_OK) return null;

        var dialog = (IFileOpenDialog)obj;

        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);

        if (title is not null)
            dialog.SetTitle(title);

        hr = dialog.Show(hwnd);
        if (hr == CANCELLED) return null;
        if (hr != S_OK) return null;

        dialog.GetResult(out IShellItem item);
        item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
        return path;
    }

    /// <summary>
    /// Shows a file-open dialog owned by <paramref name="hwnd"/>.
    /// Returns the selected path, or <c>null</c> if the user cancelled.
    /// </summary>
    public static string? PickFile(IntPtr hwnd, string? title = null,
                                   string? filterName = null, string? filterExt = null)
    {
        var clsid = CLSID_FileOpenDialog;
        var iid   = IID_IFileOpenDialog;
        int hr    = CoCreateInstance(ref clsid, IntPtr.Zero, 1,
                                     ref iid, out object obj);
        if (hr != S_OK) return null;

        var dialog = (IFileOpenDialog)obj;

        dialog.GetOptions(out uint options);
        dialog.SetOptions(options | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_FILEMUSTEXIST);

        if (title is not null)
            dialog.SetTitle(title);

        hr = dialog.Show(hwnd);
        if (hr == CANCELLED) return null;
        if (hr != S_OK) return null;

        dialog.GetResult(out IShellItem item);
        item.GetDisplayName(SIGDN_FILESYSPATH, out string path);
        return path;
    }
}
