using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HVLab.Core.Tests;

/// <summary>The app's container: every page gets its view model, and the Hyper-V parts are one service.</summary>
public class ServiceRegistrationTests
{
    private sealed class NoTheme : IThemeService { public void Apply(string theme) { } }

    private static ServiceProvider Container()
        => new ServiceCollection().AddHVLabCore()
                                  .AddSingleton<IThemeService, NoTheme>()
                                  .AddSingleton<IDialogService, FakeDialogService>()
                                  .BuildServiceProvider();

    [Fact]
    public void Every_view_model_resolves()
    {
        using var services = Container();
        Assert.NotNull(services.GetRequiredService<DashboardViewModel>());
        Assert.NotNull(services.GetRequiredService<VirtualMachinesViewModel>());
        Assert.NotNull(services.GetRequiredService<VirtualSwitchesViewModel>());
        Assert.NotNull(services.GetRequiredService<BaseVhdxViewModel>());
        Assert.NotNull(services.GetRequiredService<CreateVmViewModel>());
        Assert.NotNull(services.GetRequiredService<QuickVmViewModel>());
        Assert.NotNull(services.GetRequiredService<SettingsViewModel>());
        Assert.NotNull(services.GetRequiredService<LabsViewModel>());
    }

    [Fact]
    public void The_hyper_v_parts_are_one_instance()
    {
        using var services = Container();
        var all = services.GetRequiredService<IHyperVService>();
        Assert.Same(all, services.GetRequiredService<IHostService>());
        Assert.Same(all, services.GetRequiredService<IVmService>());
        Assert.Same(all, services.GetRequiredService<IGuestService>());
        Assert.Same(all, services.GetRequiredService<INetworkService>());
    }
}
