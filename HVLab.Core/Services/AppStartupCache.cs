using HVLab.Models;

namespace HVLab.Services;

/// <summary>
/// Cache partagé rempli pendant le splash screen pour éviter un double appel Hyper-V au démarrage.
/// </summary>
public sealed class AppStartupCache(IHyperVService hvService, IVhdxService vhdxService)
{
    public bool IsReady { get; private set; }
    private bool _consumed;

    public List<VirtualMachine> VirtualMachines { get; private set; } = [];
    public List<VirtualSwitch>  VirtualSwitches  { get; private set; } = [];
    public List<BaseVhdx>       BaseVhdxImages   { get; private set; } = [];
    public Exception?           Error            { get; private set; }
    public HyperVEnvironment    Environment      { get; private set; } = HyperVEnvironment.Ok;

    /// <summary>Pré-charge les données Hyper-V au démarrage.</summary>
    public async Task LoadAsync(string baseVhdxFolder)
    {
        IsReady   = false;
        _consumed = false;
        Error     = null;
        try
        {
            try { Environment = await hvService.GetEnvironmentStatusAsync(); }
            catch (Exception ex) { CrashLogger.Log("GetEnvironmentStatusAsync", ex); }
            if (Environment == HyperVEnvironment.FeatureMissing)
                throw new InvalidOperationException(LocalizationService.Instance["Env_FeatureMissing"]);

            var vmTask      = hvService.GetVirtualMachinesAsync();
            var swTask      = hvService.GetVirtualSwitchesAsync();
            var vhdxTask    = vhdxService.GetBaseVhdxListAsync(baseVhdxFolder);

            await Task.WhenAll(vmTask, swTask, vhdxTask);

            VirtualMachines = vmTask.Result;
            VirtualSwitches = swTask.Result;
            BaseVhdxImages  = vhdxTask.Result;
        }
        catch (Exception ex)
        {
            Error = ex;
            VirtualMachines = [];
            VirtualSwitches = [];
            BaseVhdxImages  = [];
        }
        finally
        {
            IsReady = true;
        }
    }

    /// <summary>
    /// Renvoie l'instantané du démarrage une seule fois ; les appels suivants
    /// renvoient false afin que l'appelant recharge des données fraîches.
    /// </summary>
    public bool TryConsume(out List<VirtualMachine> vms, out List<VirtualSwitch> switches,
                           out List<BaseVhdx> images, out Exception? error)
    {
        vms = VirtualMachines; switches = VirtualSwitches; images = BaseVhdxImages; error = Error;
        if (!IsReady || _consumed) return false;
        _consumed = true;
        return true;
    }
}
