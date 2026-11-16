using HVLab.Services;
using HVLab.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HVLab;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the UI-independent services (singletons) and view models (transient).
    /// The UI layer adds <see cref="IThemeService"/> and <see cref="IDialogService"/>.
    /// </summary>
    public static IServiceCollection AddHVLabCore(this IServiceCollection services)
    {
        services.AddSingleton<IPowerShellRunner, PowerShellRunner>();
        services.AddSingleton<IHyperVWmi, Services.Wmi.HyperVWmi>();
        services.AddSingleton<IHyperVService, HyperVService>();
        // Its parts, each the same instance: one PowerShell pool, one shared VM list snapshot
        services.AddSingleton<IHostService>(sp => sp.GetRequiredService<IHyperVService>());
        services.AddSingleton<IVmService>(sp => sp.GetRequiredService<IHyperVService>());
        services.AddSingleton<IGuestService>(sp => sp.GetRequiredService<IHyperVService>());
        services.AddSingleton<INetworkService>(sp => sp.GetRequiredService<IHyperVService>());
        services.AddSingleton<IVhdxService, VhdxService>();
        services.AddSingleton<INatService, NatService>();
        services.AddSingleton<AppStartupCache>();
        services.AddSingleton<LabBlueprintService>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<VirtualMachinesViewModel>();
        services.AddTransient<VirtualSwitchesViewModel>();
        services.AddTransient<BaseVhdxViewModel>();
        services.AddTransient<CreateVmViewModel>();
        services.AddTransient<QuickVmViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LabsViewModel>();
        return services;
    }
}
