using System.Runtime.InteropServices;
using System.Text;

namespace HVLab.Services;

/// <summary>
/// Windows DPAPI, current-user scope: a password protected here can only be read back by the same Windows account
/// on the same machine. Used for the optional "protected password" in the VM notes.
/// </summary>
public static partial class Dpapi
{
    private static readonly byte[] Entropy = "HV-LAB notes"u8.ToArray();

    public static string Protect(string text)
        => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), protect: true));

    /// <summary>The text, or null when it was protected by another account or machine (or is not DPAPI data).</summary>
    public static string? TryUnprotect(string base64)
    {
        try { return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(base64), protect: false)); }
        catch (Exception) { return null; }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob      = Blob(input);
        var entropyBlob = Blob(Entropy);
        try
        {
            var ok = protect
                ? CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out outBlob);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var output = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, output, 0, output.Length);
                return output;
            }
            finally { LocalFree(outBlob.Data); }
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.Data);
            Marshal.FreeHGlobal(entropyBlob.Data);
        }
    }

    private static DataBlob Blob(byte[] bytes)
    {
        var blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
        return blob;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr mem);
}
