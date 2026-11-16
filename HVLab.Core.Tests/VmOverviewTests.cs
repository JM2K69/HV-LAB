using HVLab.Models;
using HVLab.Services;
using HVLab.ViewModels;

namespace HVLab.Core.Tests;

/// <summary>The VM window's overview (screen preview, typing the password) and the host's enhanced session setting.</summary>
public class VmOverviewTests
{
    private static VmDetailsViewModel Window(FakeHyperVService hv, string state = "Running")
    {
        var d = new VmDetailsViewModel(hv, new FakeDialogService());
        d.Initialize(new VirtualMachine
        {
            Id = "VM-1", Name = "SRV-01", State = state,
            Notes = "Created by HV-LAB on 2026-11-16 10:00\nAccount: Administrator\nPassword: Lab#2026",
        });
        return d;
    }

    [Fact]
    public async Task No_screen_when_the_vm_is_off()
    {
        var w = Window(new FakeHyperVService(), "Off");
        await w.RefreshThumbnailAsync(1024, 768);
        Assert.Null(w.Thumbnail);
    }

    [Fact]
    public async Task The_password_is_typed_with_the_chosen_layout_then_enter()
    {
        var hv = new FakeHyperVService();
        var w = Window(hv);
        w.TypingLanguage = "en-US";
        await w.TypePasswordCommand.ExecuteAsync(null);
        await w.SendCtrlAltDelCommand.ExecuteAsync(null);
        Assert.Equal(["type VM-1 Lab#2026 en-US True", "cad VM-1"], hv.Calls);
    }

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
