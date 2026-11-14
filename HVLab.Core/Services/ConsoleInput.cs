namespace HVLab.Services;

/// <summary>
/// Input of the integrated console (unit-tested): physical keys become PS/2 scancodes, so the guest applies
/// its own keyboard layout (an AZERTY guest gets AZERTY characters), and pointer positions on the zoomed
/// screen become guest pixels.
/// </summary>
public static class ConsoleInput
{
    /// <summary>Zoom levels of the console in %, 0 = fit to the window.</summary>
    public static IReadOnlyList<int> ZoomLevels { get; } = [0, 50, 75, 100, 125, 150, 200];

    /// <summary>
    /// Make (key down) or break (key up = code | 0x80) scancode of a key, with the 0xE0 prefix of extended keys
    /// (arrows, right Ctrl/Alt, Insert, Delete…). Empty for keys without a set-1 scancode.
    /// </summary>
    public static byte[] Scancodes(uint scanCode, bool extended, bool keyUp)
    {
        if (scanCode is 0 or > 0x7F) return [];
        var code = (byte)(keyUp ? scanCode | 0x80 : scanCode);
        return extended ? [0xE0, code] : [code];
    }

    private const uint Ctrl = 0x1D, Alt = 0x38, Left = 0x4B;

    /// <summary>
    /// Ctrl+Alt+Left (as in vmconnect): gives keyboard and mouse back to the host instead of reaching the guest.
    /// Left is the extended arrow (0xE0 0x4B), not the numeric keypad 4; either Ctrl and either Alt count.
    /// </summary>
    public static bool IsReleaseChord(IEnumerable<(uint Code, bool Extended)> held, uint scanCode, bool extended)
    {
        if (scanCode != Left || !extended) return false;
        var codes = held.Select(k => k.Code).ToHashSet();
        return codes.Contains(Ctrl) && codes.Contains(Alt);
    }

    /// <summary>Pointer position on the displayed screen → guest pixel, clamped to the guest screen.</summary>
    public static (int X, int Y) ToGuest(double x, double y, double shownWidth, double shownHeight, int guestWidth, int guestHeight)
    {
        if (shownWidth <= 0 || shownHeight <= 0 || guestWidth <= 0 || guestHeight <= 0) return (0, 0);
        var gx = (int)Math.Floor(x / shownWidth * guestWidth);
        var gy = (int)Math.Floor(y / shownHeight * guestHeight);
        return (Math.Clamp(gx, 0, guestWidth - 1), Math.Clamp(gy, 0, guestHeight - 1));
    }

    /// <summary>Next zoom level up or down (Ctrl + wheel); from "fit", up starts at 100 %.</summary>
    public static int StepZoom(int current, bool up)
    {
        var steps = ZoomLevels.Where(z => z > 0).ToList();
        if (current == 0) return up ? 100 : steps[0];
        var i = steps.IndexOf(current);
        if (i < 0) return 100;
        return steps[Math.Clamp(i + (up ? 1 : -1), 0, steps.Count - 1)];
    }

    /// <summary>Relative move of a PS/2 mouse, split in steps of at most ±127.</summary>
    public static IEnumerable<(sbyte Dx, sbyte Dy)> RelativeSteps(int dx, int dy)
    {
        while (dx != 0 || dy != 0)
        {
            var sx = (sbyte)Math.Clamp(dx, -127, 127);
            var sy = (sbyte)Math.Clamp(dy, -127, 127);
            yield return (sx, sy);
            dx -= sx;
            dy -= sy;
        }
    }
}
