using System.Runtime.InteropServices;

namespace HVLab.Services;

/// <summary>
/// Text → PS/2 scancodes for Msvm_Keyboard.TypeScancodes, for the keyboard layout of the guest.
/// Msvm_Keyboard.TypeText assumes a US guest, so on an AZERTY guest "a" would come out as "q".
/// Here each character is looked up in the guest's layout (loaded from Windows by its KLID, e.g.
/// 0000040C for French) to find the physical key and the modifiers that produce it.
/// </summary>
public static partial class KeyboardScancodes
{
    private const byte LeftShift = 0x2A, RightAlt = 0x38, Enter = 0x1C, Extended = 0xE0, Break = 0x80;

    /// <summary>KLID of the guest layout from the language of the VM's answer file (fr-FR → 0000040C).</summary>
    public static string Klid(string language) => language switch
    {
        "fr-FR" => "0000040C",
        "en-GB" => "00000809",
        "de-DE" => "00000407",
        "es-ES" => "0000040A",
        "it-IT" => "00000410",
        _       => "00000409",   // en-US
    };

    /// <summary>
    /// Make/break scancodes typing <paramref name="text"/> on a guest using <paramref name="klid"/>.
    /// Throws when a character cannot be typed on that layout.
    /// </summary>
    public static byte[] Build(string text, string klid, bool pressEnter)
    {
        // A layout loaded only for this lookup is unloaded afterwards, so it never shows up in the user's input list
        var before = LoadedLayouts();
        var layout = LoadKeyboardLayout(klid, KLF_NOTELLSHELL);
        if (layout == 0) throw new InvalidOperationException($"Keyboard layout {klid} is not available");
        try { return Scancodes(text, layout, pressEnter); }
        finally { if (!before.Contains(layout)) UnloadKeyboardLayout(layout); }
    }

    private static HashSet<nint> LoadedLayouts()
    {
        var list = new nint[64];
        var count = GetKeyboardLayoutList(list.Length, list);
        return list.Take(count).ToHashSet();
    }

    private static byte[] Scancodes(string text, nint layout, bool pressEnter)
    {
        var codes = new List<byte>();
        foreach (var c in text)
        {
            var vkScan = VkKeyScanEx((ushort)c, layout);
            if (vkScan == -1) throw new ArgumentException(LocalizationService.T("Err_KeyNotTypable", c));
            var vk       = (uint)(vkScan & 0xFF);
            var shift    = (vkScan & 0x100) != 0;
            var altGr    = (vkScan & 0x600) == 0x600;   // Ctrl+Alt = AltGr
            var scancode = (byte)MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, layout);
            if (scancode == 0) throw new ArgumentException(LocalizationService.T("Err_KeyNotTypable", c));

            if (altGr) codes.AddRange([Extended, RightAlt]);
            if (shift) codes.Add(LeftShift);
            codes.Add(scancode);
            codes.Add((byte)(scancode | Break));
            if (shift) codes.Add(LeftShift | Break);
            if (altGr) codes.AddRange([Extended, RightAlt | Break]);
        }
        if (pressEnter) codes.AddRange([Enter, Enter | Break]);
        return codes.ToArray();
    }

    private const uint KLF_NOTELLSHELL = 0x00000080, MAPVK_VK_TO_VSC = 0;

    [LibraryImport("user32.dll", EntryPoint = "LoadKeyboardLayoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadKeyboardLayout(string pwszKLID, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "VkKeyScanExW")]
    private static partial short VkKeyScanEx(ushort ch, nint dwhkl);   // WCHAR

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    private static partial uint MapVirtualKeyEx(uint uCode, uint uMapType, nint dwhkl);

    [LibraryImport("user32.dll")]
    private static partial int GetKeyboardLayoutList(int nBuff, [Out] nint[] lpList);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnloadKeyboardLayout(nint hkl);
}
