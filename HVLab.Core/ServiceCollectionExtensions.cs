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
        services.AddSingleton<IHyperVService, HyperVService>();
        services.AddSingleton<IVhdxService, VhdxService>();
        services.AddSingleton<INatService, NatService>();
        services.AddSingleton<AppStartupCache>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<VirtualMachinesViewModel>();
        services.AddTransient<VirtualSwitchesViewModel>();
        services.AddTransient<BaseVhdxViewModel>();
        services.AddTransient<CreateVmViewModel>();
        services.AddTransient<QuickVmViewModel>();
        services.AddTransient<SettingsViewModel>();
        return services;
    }
}
