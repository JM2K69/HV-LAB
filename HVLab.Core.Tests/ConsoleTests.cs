using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

public class ConsoleInputTests
{
    [Theory]
    [InlineData(0x1Eu, false, false, "1E")]     // A down
    [InlineData(0x1Eu, false, true,  "9E")]     // A up = code | 0x80
    [InlineData(0x48u, true,  false, "E048")]   // Up arrow (extended)
    [InlineData(0x48u, true,  true,  "E0C8")]
    [InlineData(0x1Du, true,  false, "E01D")]   // right Ctrl
    public void Scancodes_are_set_1_make_and_break_codes(uint scan, bool extended, bool up, string expected)
        => Assert.Equal(expected, Convert.ToHexString(ConsoleInput.Scancodes(scan, extended, up)));

    [Theory]
    [InlineData(0u)]
    [InlineData(0x80u)]
    [InlineData(0xE11Du)]
    public void Keys_without_a_scancode_send_nothing(uint scan)
        => Assert.Empty(ConsoleInput.Scancodes(scan, false, false));

    [Fact]
    public void Pointer_positions_become_guest_pixels_whatever_the_zoom()
    {
        // 1024×768 guest shown at 200 % (2048×1536)
        Assert.Equal((100, 50), ConsoleInput.ToGuest(200, 100, 2048, 1536, 1024, 768));
        // shown at 50 % (512×384)
        Assert.Equal((512, 384), ConsoleInput.ToGuest(256, 192, 512, 384, 1024, 768));
        // fit to an odd size
        Assert.Equal((0, 0), ConsoleInput.ToGuest(0, 0, 900.5, 675.4, 1024, 768));
    }

    [Fact]
    public void Pointer_positions_are_clamped_to_the_guest_screen()
    {
        Assert.Equal((1023, 767), ConsoleInput.ToGuest(5000, 5000, 1024, 768, 1024, 768));
        Assert.Equal((0, 0), ConsoleInput.ToGuest(-30, -1, 1024, 768, 1024, 768));
        Assert.Equal((0, 0), ConsoleInput.ToGuest(10, 10, 0, 0, 1024, 768));
    }

    [Theory]
    [InlineData(0, true, 100)]     // from fit, zoom in starts at 100 %
    [InlineData(0, false, 50)]
    [InlineData(100, true, 125)]
    [InlineData(100, false, 75)]
    [InlineData(200, true, 200)]   // stays at the bounds
    [InlineData(50, false, 50)]
    public void Ctrl_wheel_steps_through_the_zoom_levels(int current, bool up, int expected)
        => Assert.Equal(expected, ConsoleInput.StepZoom(current, up));

    [Fact]
    public void Ps2_relative_moves_are_split_in_steps_of_127()
    {
        var steps = ConsoleInput.RelativeSteps(300, -10).ToList();
        Assert.Equal([((sbyte)127, (sbyte)-10), ((sbyte)127, (sbyte)0), ((sbyte)46, (sbyte)0)], steps);
        Assert.Equal(300, steps.Sum(s => s.Dx));
        Assert.Empty(ConsoleInput.RelativeSteps(0, 0));
    }
}

public class ConsoleViewModelTests
{
    private static VirtualMachine Vm(string state)
        => new() { Id = "VM-1", Name = "SRV-01", State = state };

    private static VmDetailsViewModel Console(FakeHyperVService hv, string state = "Running")
    {
        var d = new VmDetailsViewModel(hv, new FakeDialogService());
        d.Initialize(Vm(state));
        return d;
    }

    [Fact]
    public async Task Keys_go_to_the_vm_as_scancodes()
    {
        var hv = new FakeHyperVService();
        var c = Console(hv);
        await c.SendKeyAsync(0x48, extended: true, keyUp: true);
        await c.SendKeyAsync(0, extended: false, keyUp: false);   // no scancode: nothing sent
        Assert.Equal(["keys VM-1 E0C8"], hv.Calls);
    }

    [Fact]
    public async Task Mouse_moves_buttons_and_wheel_go_to_the_vm()
    {
        var hv = new FakeHyperVService();
        var c = Console(hv);
        c.MoveMouse(10, 20);
        await c.MouseButtonAsync(1, true);
        await c.MouseButtonAsync(1, false);
        await c.MouseWheelAsync(-120);
        Assert.Equal(["move VM-1 10,20", "button VM-1 1 True", "button VM-1 1 False", "wheel VM-1 -120"], hv.Calls);
    }

    [Fact]
    public async Task Mouse_moves_are_coalesced_while_one_is_in_flight()
    {
        var hv = new FakeHyperVService { MoveGate = new TaskCompletionSource() };
        var c = Console(hv);
        c.MoveMouse(1, 1);   // in flight
        c.MoveMouse(2, 2);   // replaced by the next one
        c.MoveMouse(3, 3);
        hv.MoveGate.SetResult();
        for (var i = 0; i < 100 && hv.Calls.Count < 2; i++) await Task.Delay(10);
        Assert.Equal(["move VM-1 1,1", "move VM-1 3,3"], hv.Calls);
    }

    [Fact]
    public void Zoom_starts_on_fit_and_steps_with_ctrl_wheel()
    {
        var c = Console(new FakeHyperVService());
        Assert.True(c.IsFit);
        Assert.Equal(LocalizationService.T("CON_ZoomFit"), c.ZoomOptions[0]);
        c.StepZoom(up: true);
        Assert.Equal(100, c.Zoom);
        Assert.Equal("100 %", c.ZoomOptions[c.ZoomIndex]);
        c.StepZoom(up: true);
        Assert.Equal(125, c.Zoom);
    }

    [Fact]
    public async Task Typing_the_clipboard_uses_the_chosen_layout_and_skips_empty_text()
    {
        var hv = new FakeHyperVService();
        var c = Console(hv);
        await c.TypeTextAsync(null);
        Assert.Empty(hv.Calls);
        Assert.Equal(LocalizationService.T("CON_ClipboardEmpty"), c.Status);
        await c.TypeTextAsync("ping 10.0.0.1");
        Assert.Equal([$"type VM-1 ping 10.0.0.1 {c.TypingLanguage} False"], hv.Calls);
    }

    [Fact]
    public async Task No_frame_when_the_vm_is_off()
    {
        var c = Console(new FakeHyperVService(), "Off");
        await c.RefreshConsoleAsync();
        Assert.Null(c.ConsoleFrame);
    }
}

public class EnhancedSessionSettingsTests
{
    private sealed class NoTheme : IThemeService { public void Apply(string theme) { } }

    [Fact]
    public async Task Loading_reads_the_host_without_writing_it_and_toggling_writes_it()
    {
        var hv = new FakeHyperVService { EnhancedSessionHost = false };
        var s = new SettingsViewModel(new NoTheme(), hv);
        Assert.False(s.EnhancedSessionLoaded);

        await s.LoadHostSettingsAsync();
        Assert.True(s.EnhancedSessionLoaded);
        Assert.False(s.EnhancedSession);
        Assert.Empty(hv.Calls);

        s.EnhancedSession = true;
        Assert.Equal(["enhanced True"], hv.Calls);
        Assert.True(hv.EnhancedSessionHost);
    }
}
