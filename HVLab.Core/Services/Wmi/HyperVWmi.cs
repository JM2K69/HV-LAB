using System.Collections.Concurrent;
using System.Management;
using HVLab.Models;
using static HVLab.Services.LocalizationService;

namespace HVLab.Services.Wmi;

/// <summary>
/// Direct WMI access to Hyper-V (root\virtualization\v2) and to the host network (root\StandardCimv2) for the
/// frequent operations: a query takes a few tens of milliseconds where Get-VM takes over a second.
/// Heavy, rare operations (base images, VM creation, settings, export) stay in PowerShell.
/// All methods are synchronous and are called from a worker thread (see <see cref="HyperVService"/>).
/// </summary>
public sealed class HyperVWmi : IHyperVWmi
{
    private const string V2    = @"root\virtualization\v2";
    private const string Std   = @"root\StandardCimv2";
    private const string Cimv2 = @"root\cimv2";

    private readonly ConcurrentDictionary<string, ManagementScope> _scopes = new();

    private ManagementScope Scope(string ns) => _scopes.GetOrAdd(ns, n =>
    {
        var scope = new ManagementScope($@"\\.\{n}");
        scope.Connect();
        return scope;
    });

    /// <summary>Runs a WQL query and copies each object's properties (the COM objects are released at once).</summary>
    private List<Dictionary<string, object?>> Rows(string ns, string wql)
    {
        using var searcher = new ManagementObjectSearcher(Scope(ns), new ObjectQuery(wql));
        using var results  = searcher.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementBaseObject o in results)
            using (o)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in o.Properties) row[p.Name] = p.Value;
                rows.Add(row);
            }
        return rows;
    }

    private ManagementObject? First(string ns, string wql)
    {
        using var searcher = new ManagementObjectSearcher(Scope(ns), new ObjectQuery(wql));
        using var results  = searcher.Get();
        return results.Cast<ManagementObject>().FirstOrDefault();
    }

    private static string S(Dictionary<string, object?> row, string name) => row.GetValueOrDefault(name)?.ToString() ?? "";
    private static long   L(Dictionary<string, object?> row, string name) => row.GetValueOrDefault(name) is { } v ? Convert.ToInt64(v) : 0;
    private static long?  N(Dictionary<string, object?> row, string name) => row.GetValueOrDefault(name) is { } v ? Convert.ToInt64(v) : null;
    private static IEnumerable<string> Strings(Dictionary<string, object?> row, string name)
        => row.GetValueOrDefault(name) is string[] a ? a : [];

    /// <summary>VM IDs go into WQL strings: only a GUID is accepted.</summary>
    private static string Id(string vmId)
        => Guid.TryParse(vmId, out var g) ? g.ToString().ToUpperInvariant() : throw new ArgumentException($"Invalid VM id '{vmId}'");

    // ─── Environment ────────────────────────────────────────────────────────

    public HyperVEnvironment GetEnvironment()
    {
        // HV-LAB creates VMs with the Hyper-V PowerShell module: without it the host is not usable
        var module = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\Modules\Hyper-V");
        var service = Rows(Cimv2, "SELECT State FROM Win32_Service WHERE Name = 'vmms'");
        if (!Directory.Exists(module) || service.Count == 0) return HyperVEnvironment.FeatureMissing;
        return S(service[0], "State") == "Running" ? HyperVEnvironment.Ok : HyperVEnvironment.ServiceStopped;
    }

    // ─── VM list ────────────────────────────────────────────────────────────

    public List<VirtualMachine> GetVirtualMachines()
    {
        var vms = Rows(V2, "SELECT * FROM Msvm_SummaryInformation")
            .Select(r => new VirtualMachine
            {
                Id             = S(r, "Name").ToUpperInvariant(),
                Name           = S(r, "ElementName"),
                State          = WmiMap.StateName(L(r, "EnabledState")),
                Generation     = WmiMap.Generation(S(r, "VirtualSystemSubType")),
                ProcessorCount = (int)L(r, "NumberOfProcessors"),
                CpuUsage       = (int)L(r, "ProcessorLoad"),
                MemoryMB       = L(r, "MemoryUsage"),
                Uptime         = WmiMap.FormatUptime(L(r, "UpTime")),
                Heartbeat      = WmiMap.Heartbeat(N(r, "Heartbeat")),
                Notes          = S(r, "Notes"),
                GuestOs        = S(r, "GuestOperatingSystem"),
            })
            .Where(v => v.Id.Length > 0)
            .ToList();
        if (vms.Count == 0) return vms;
        var byId = vms.ToDictionary(v => v.Id);

        // Memory of stopped VMs: startup RAM of the realized settings (snapshot settings belong to other IDs)
        foreach (var m in Rows(V2, "SELECT InstanceID, VirtualQuantity FROM Msvm_MemorySettingData"))
            if (WmiMap.OwnerId(S(m, "InstanceID")) is { } id && byId.TryGetValue(id, out var vm) && vm.MemoryMB == 0)
                vm.MemoryMB = L(m, "VirtualQuantity");

        // Switch of the first adapter: port allocation → Msvm_VirtualEthernetSwitch.Name
        var switchNames = Rows(V2, "SELECT Name, ElementName FROM Msvm_VirtualEthernetSwitch")
            .ToDictionary(r => S(r, "Name").ToUpperInvariant(), r => S(r, "ElementName"));
        foreach (var p in Rows(V2, "SELECT InstanceID, HostResource FROM Msvm_EthernetPortAllocationSettingData"))
            if (WmiMap.OwnerId(S(p, "InstanceID")) is { } id && byId.TryGetValue(id, out var vm) && vm.SwitchName.Length == 0
                && WmiMap.PathKey(Strings(p, "HostResource").FirstOrDefault(), "Name") is { } sw
                && switchNames.TryGetValue(sw.ToUpperInvariant(), out var swName))
                vm.SwitchName = swName;

        // VLAN of the adapters (access id or trunk list)
        foreach (var g in Rows(V2, "SELECT InstanceID, OperationMode, AccessVlanId, TrunkVlanIdArray FROM Msvm_EthernetSwitchPortVlanSettingData")
                     .GroupBy(r => WmiMap.OwnerId(S(r, "InstanceID")) ?? ""))
            if (byId.TryGetValue(g.Key, out var vm))
                vm.VlanInfo = string.Join(", ", g.Select(r => WmiMap.VlanText(L(r, "OperationMode"), L(r, "AccessVlanId"),
                    (r.GetValueOrDefault("TrunkVlanIdArray") as Array)?.Cast<object>().Select(Convert.ToInt64)))
                    .Where(t => t.Length > 0).Distinct());

        // Enhanced session: 2 = available (guest ready), 6 = enabled but the guest is not ready, 3 = off
        foreach (var r in Rows(V2, "SELECT Name, EnhancedSessionModeState FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'"))
            if (byId.TryGetValue(S(r, "Name").ToUpperInvariant(), out var vm))
                vm.EnhancedSessionReady = L(r, "EnhancedSessionModeState") == 2;

        FillAddresses(byId);
        FillGroups(byId);
        return vms;
    }

    /// <summary>IP: the guest's own report (KVP), else the host neighbour cache by MAC, else the static IP in the notes.</summary>
    private void FillAddresses(Dictionary<string, VirtualMachine> byId)
    {
        foreach (var g in Rows(V2, "SELECT InstanceID, IPAddresses FROM Msvm_GuestNetworkAdapterConfiguration")
                     .GroupBy(r => WmiMap.OwnerId(S(r, "InstanceID")) ?? ""))
            if (byId.TryGetValue(g.Key, out var vm))
                vm.IpAddress = WmiMap.FirstIpv4(g.SelectMany(r => Strings(r, "IPAddresses"))) ?? "";

        var missing = byId.Values.Where(v => v.IpAddress.Length == 0 && v.State == "Running").ToList();
        if (missing.Count > 0)
        {
            try
            {
                var macs = Rows(V2, "SELECT InstanceID, Address FROM Msvm_SyntheticEthernetPortSettingData")
                    .Where(r => S(r, "Address").Length > 0)
                    .Select(r => (Owner: WmiMap.OwnerId(S(r, "InstanceID")), Mac: WmiMap.NormalizeMac(S(r, "Address"))))
                    .ToList();
                // States 2-5: probe, delay, stale, reachable (not unreachable/incomplete/permanent)
                var neighbours = Rows(Std, "SELECT IPAddress, LinkLayerAddress, State FROM MSFT_NetNeighbor WHERE AddressFamily = 2")
                    .Where(r => L(r, "State") is >= 2 and <= 5)
                    .GroupBy(r => WmiMap.NormalizeMac(S(r, "LinkLayerAddress")))
                    .ToDictionary(g => g.Key, g => WmiMap.FirstIpv4(g.Select(r => S(r, "IPAddress"))));
                foreach (var vm in missing)
                    vm.IpAddress = macs.Where(m => m.Owner == vm.Id)
                                       .Select(m => neighbours.GetValueOrDefault(m.Mac))
                                       .FirstOrDefault(ip => ip is not null) ?? "";
            }
            catch (ManagementException) { /* the neighbour cache is a best-effort fallback */ }
        }

        foreach (var vm in byId.Values.Where(v => v.IpAddress.Length == 0))
            vm.IpAddress = VmNotes.Read(vm.Notes).IpAddress;
    }

    /// <summary>Hyper-V VM groups (Msvm_VirtualSystemCollection) the VMs belong to.</summary>
    private void FillGroups(Dictionary<string, VirtualMachine> byId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope(V2), new ObjectQuery("SELECT * FROM Msvm_VirtualSystemCollection"));
            using var groups   = searcher.Get();
            foreach (ManagementObject group in groups)
                using (group)
                {
                    var name = group["ElementName"]?.ToString() ?? "";
                    using var members = group.GetRelated("Msvm_ComputerSystem");
                    foreach (ManagementBaseObject member in members)
                        using (member)
                            if (byId.TryGetValue(member["Name"]?.ToString()?.ToUpperInvariant() ?? "", out var vm) && vm.GroupName.Length == 0)
                                vm.GroupName = name;
                }
        }
        catch (ManagementException) { /* groups are optional (older hosts) */ }
    }

    // ─── Power actions ──────────────────────────────────────────────────────

    public void ChangeState(string vmId, VmPowerAction action)
    {
        var id = Id(vmId);
        if (action is VmPowerAction.Shutdown or VmPowerAction.Restart)
        {
            using var shutdown = First(V2, $"SELECT * FROM Msvm_ShutdownComponent WHERE SystemName = '{id}'")
                ?? throw new InvalidOperationException(T("Err_NoShutdownComponent"));
            using var args = shutdown.GetMethodParameters(action == VmPowerAction.Shutdown ? "InitiateShutdown" : "InitiateReboot");
            args["Force"]  = true;
            args["Reason"] = "HV-LAB";
            using var result = shutdown.InvokeMethod(action == VmPowerAction.Shutdown ? "InitiateShutdown" : "InitiateReboot", args, null);
            CheckResult(result);
            if (action == VmPowerAction.Shutdown) WaitForState(id, 3, TimeSpan.FromMinutes(2));
            return;
        }

        ushort requested = action switch
        {
            VmPowerAction.Start or VmPowerAction.Resume => 2,
            VmPowerAction.TurnOff                       => 3,
            VmPowerAction.Save                          => 6,
            VmPowerAction.Pause                         => 9,
            VmPowerAction.Reset                         => 11,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        using var vm = First(V2, $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{id}'")
            ?? throw new InvalidOperationException(T("Err_VmNotFound", vmId));
        using var inArgs = vm.GetMethodParameters("RequestStateChange");
        inArgs["RequestedState"] = requested;
        using var outArgs = vm.InvokeMethod("RequestStateChange", inArgs, null);
        CheckResult(outArgs, TimeSpan.FromMinutes(5));
    }

    /// <summary>Return value 0 = done, 4096 = job started (waited for), anything else = failure.</summary>
    private void CheckResult(ManagementBaseObject result, TimeSpan? jobTimeout = null)
    {
        var rv = Convert.ToUInt32(result["ReturnValue"]);
        if (rv == 0) return;
        if (rv != 4096) throw new InvalidOperationException(ErrorText(rv));

        using var job = new ManagementObject(Scope(V2), new ManagementPath(result["Job"].ToString()), null);
        var deadline = DateTime.UtcNow + (jobTimeout ?? TimeSpan.FromMinutes(5));
        while (true)
        {
            job.Get();
            var state = Convert.ToUInt16(job["JobState"]);
            if (state == 7) return;                                   // Completed
            if (state is >= 8 and <= 10)                              // Terminated, Killed, Exception
                throw new InvalidOperationException(job["ErrorDescription"]?.ToString() is { Length: > 0 } d ? d : ErrorText(state));
            if (DateTime.UtcNow > deadline) throw new TimeoutException(T("Err_WmiJobTimeout"));
            Thread.Sleep(200);
        }
    }

    private static string ErrorText(uint code) => code switch
    {
        32775 => T("Err_WmiInvalidState"),
        32769 => T("Err_WmiAccessDenied"),
        _     => T("Err_WmiCode", code),
    };

    private void WaitForState(string id, long state, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var rows = Rows(V2, $"SELECT EnabledState FROM Msvm_ComputerSystem WHERE Name = '{id}'");
            if (rows.Count == 0 || L(rows[0], "EnabledState") == state) return;
            Thread.Sleep(500);
        }
    }

    // ─── Screen and keyboard ────────────────────────────────────────────────

    public VmThumbnail? GetThumbnail(string vmId, int maxWidth, int maxHeight)
    {
        var id = Id(vmId);
        var head = Rows(V2, $"SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Msvm_VideoHead WHERE SystemName = '{id}'")
            .FirstOrDefault(r => L(r, "CurrentHorizontalResolution") > 0 && L(r, "CurrentVerticalResolution") > 0);
        var (w, h) = head is null ? (maxWidth, maxHeight)
            : WmiMap.FitThumbnail((int)L(head, "CurrentHorizontalResolution"), (int)L(head, "CurrentVerticalResolution"), maxWidth, maxHeight);

        using var settings = First(V2, $"SELECT * FROM Msvm_VirtualSystemSettingData WHERE InstanceID = 'Microsoft:{id}'");
        using var service  = First(V2, "SELECT * FROM Msvm_VirtualSystemManagementService");
        if (settings is null || service is null) return null;
        using var args = service.GetMethodParameters("GetVirtualSystemThumbnailImage");
        args["TargetSystem"] = settings.Path.Path;
        args["WidthPixels"]  = (ushort)w;
        args["HeightPixels"] = (ushort)h;
        using var result = service.InvokeMethod("GetVirtualSystemThumbnailImage", args, null);
        if (Convert.ToUInt32(result["ReturnValue"]) != 0 || result["ImageData"] is not byte[] data || data.Length < w * h * 2)
            return null;
        return new VmThumbnail(w, h, WmiMap.Rgb565ToBgra(data, w, h));
    }

    /// <summary>Raw scancodes (make and break codes): every key of the console goes through here.</summary>
    public void TypeScancodes(string vmId, byte[] scancodes)
        => Invoke("Msvm_Keyboard", vmId, "TypeScancodes", ("Scancodes", scancodes));

    public void TypeCtrlAltDel(string vmId)
    {
        using var keyboard = Keyboard(vmId);
        using var result = keyboard.InvokeMethod("TypeCtrlAltDel", keyboard.GetMethodParameters("TypeCtrlAltDel"), null);
        CheckResult(result);
    }

    private ManagementObject Keyboard(string vmId)
        => First(V2, $"SELECT * FROM Msvm_Keyboard WHERE SystemName = '{Id(vmId)}'")
           ?? throw new InvalidOperationException(T("Err_NoKeyboard"));

    // ─── Integrated console: mouse ──────────────────────────────────────────

    // Console input sends many small calls: the device objects are cached per VM (dropped on failure)
    private readonly ConcurrentDictionary<string, ManagementObject?> _devices = new();
    private readonly ConcurrentDictionary<string, (int X, int Y)> _ps2Positions = new();

    /// <summary>Device of a running VM; a missing device is not cached (it appears once the guest has booted).</summary>
    private ManagementObject? Device(string cls, string vmId)
    {
        var key = $"{cls}|{Id(vmId)}";
        if (_devices.TryGetValue(key, out var cached) && cached is not null) return cached;
        var device = First(V2, $"SELECT * FROM {cls} WHERE SystemName = '{Id(vmId)}'");
        if (device is not null) _devices[key] = device;
        return device;
    }

    /// <summary>Calls a device method; on a stale cached object the device is looked up again once.</summary>
    private void Invoke(string cls, string vmId, string method, params (string Name, object Value)[] args)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var device = Device(cls, vmId)
                ?? throw new InvalidOperationException(T(cls == "Msvm_Keyboard" ? "Err_NoKeyboard" : "Err_NoMouse"));
            try
            {
                using var inArgs = device.GetMethodParameters(method);
                foreach (var (name, value) in args) inArgs[name] = value;
                using var result = device.InvokeMethod(method, inArgs, null);
                return;
            }
            catch (ManagementException) when (attempt == 0) { _devices.TryRemove($"{cls}|{Id(vmId)}", out _); }
        }
    }

    /// <summary>Synthetic mouse (absolute pixels) when the guest has one, else PS/2 relative moves.</summary>
    private bool HasSyntheticMouse(string vmId) => Device("Msvm_SyntheticMouse", vmId) is not null;

    public void MouseMove(string vmId, int x, int y)
    {
        if (HasSyntheticMouse(vmId))
        {
            Invoke("Msvm_SyntheticMouse", vmId, "SetAbsolutePosition", ("HorizontalPosition", x), ("VerticalPosition", y));
            return;
        }
        var last = _ps2Positions.GetValueOrDefault(Id(vmId));
        foreach (var (dx, dy) in ConsoleInput.RelativeSteps(x - last.X, y - last.Y))
            Invoke("Msvm_Ps2Mouse", vmId, "SetRelativePosition", ("HorizontalDelta", dx), ("VerticalDelta", dy));
        _ps2Positions[Id(vmId)] = (x, y);
    }

    /// <summary>Button 1 = left, 2 = right, 3 = middle.</summary>
    public void MouseButton(string vmId, int button, bool down)
    {
        if (HasSyntheticMouse(vmId))
            Invoke("Msvm_SyntheticMouse", vmId, "SetButtonState", ("ButtonIndex", (uint)button), ("IsDown", down));
        else
            Invoke("Msvm_Ps2Mouse", vmId, "SetButtonState", ("ButtonIndex", (uint)button), ("ButtonState", down));
    }

    public void MouseWheel(string vmId, int delta)
    {
        if (HasSyntheticMouse(vmId))
            Invoke("Msvm_SyntheticMouse", vmId, "SetScrollPosition", ("ScrollPositionDelta", delta));
        else
            Invoke("Msvm_Ps2Mouse", vmId, "SetScrollPosition", ("ScrollPositionDelta", (sbyte)Math.Clamp(delta / 40, -127, 127)));
    }

    /// <summary>Host setting that lets vmconnect open enhanced sessions (clipboard, drives, dynamic resolution).</summary>
    public bool GetEnhancedSessionHost()
        => Rows(V2, "SELECT EnhancedSessionModeEnabled FROM Msvm_VirtualSystemManagementServiceSettingData")
            .FirstOrDefault()?.GetValueOrDefault("EnhancedSessionModeEnabled") is true;

    // ─── Checkpoints ────────────────────────────────────────────────────────

    public List<CheckpointRow> GetCheckpoints(string vmId)
    {
        var id = Id(vmId);
        var checkpoints = Rows(V2, "SELECT ConfigurationID, ElementName, CreationTime, Parent FROM Msvm_VirtualSystemSettingData " +
                                   $"WHERE VirtualSystemIdentifier = '{id}' AND VirtualSystemType = 'Microsoft:Hyper-V:Snapshot:Realized'")
            .Select(r => new VmCheckpoint(
                S(r, "ConfigurationID").ToUpperInvariant(),
                S(r, "ElementName"),
                WmiMap.OwnerId(WmiMap.PathKey(S(r, "Parent"), "InstanceID")),
                S(r, "CreationTime") is { Length: > 0 } t ? ManagementDateTimeConverter.ToDateTime(t) : null))
            .ToList();
        var current = Rows(V2, $"SELECT Parent FROM Msvm_VirtualSystemSettingData WHERE InstanceID = 'Microsoft:{id}'").FirstOrDefault();
        var currentParent = current is null ? null : WmiMap.OwnerId(WmiMap.PathKey(S(current, "Parent"), "InstanceID"));
        return WmiMap.CheckpointTree(checkpoints, currentParent);
    }

    // ─── Switches and NAT ───────────────────────────────────────────────────

    public List<VirtualSwitch> GetSwitches()
    {
        var notes = Rows(V2, "SELECT VirtualSystemIdentifier, Notes FROM Msvm_VirtualEthernetSwitchSettingData")
            .GroupBy(r => S(r, "VirtualSystemIdentifier").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => string.Join(Environment.NewLine, Strings(g.First(), "Notes")));
        var ports = Rows(V2, "SELECT InstanceID, HostResource FROM Msvm_EthernetPortAllocationSettingData");
        var vlans = Rows(V2, "SELECT InstanceID, OperationMode, AccessVlanId FROM Msvm_EthernetSwitchPortVlanSettingData");
        // External adapter: Hyper-V knows its description, the friendly name comes from MSFT_NetAdapter
        var externalNames = Rows(V2, "SELECT DeviceID, ElementName FROM Msvm_ExternalEthernetPort")
            .Concat(SafeRows(V2, "SELECT DeviceID, ElementName FROM Msvm_WiFiPort"))
            .ToDictionary(r => S(r, "DeviceID"), r => S(r, "ElementName"), StringComparer.OrdinalIgnoreCase);
        var adapterNames = SafeRows(Std, "SELECT Name, InterfaceDescription FROM MSFT_NetAdapter")
            .GroupBy(r => S(r, "InterfaceDescription"))
            .ToDictionary(g => g.Key, g => S(g.First(), "Name"));

        var result = new List<VirtualSwitch>();
        foreach (var sw in Rows(V2, "SELECT Name, ElementName FROM Msvm_VirtualEthernetSwitch"))
        {
            var guid = S(sw, "Name").ToUpperInvariant();
            var own  = ports.Where(p => WmiMap.OwnerId(S(p, "InstanceID")) == guid).ToList();
            var external = own.Select(p => Strings(p, "HostResource").FirstOrDefault() ?? "")
                              .FirstOrDefault(h => h.Contains("Msvm_ExternalEthernetPort") || h.Contains("Msvm_WiFiPort"));
            var hostPort = own.FirstOrDefault(p => Strings(p, "HostResource").Any(h => h.Contains("Msvm_ComputerSystem")));
            var description = WmiMap.PathKey(external, "DeviceID") is { } dev ? externalNames.GetValueOrDefault(dev, "") : "";
            var hostVlan = hostPort is null ? null
                : vlans.FirstOrDefault(v => S(v, "InstanceID").StartsWith(S(hostPort, "InstanceID"), StringComparison.OrdinalIgnoreCase));
            result.Add(new VirtualSwitch
            {
                Name              = S(sw, "ElementName"),
                SwitchType        = WmiMap.SwitchType(external is not null, hostPort is not null),
                Notes             = notes.GetValueOrDefault(guid, ""),
                NetAdapterName    = adapterNames.GetValueOrDefault(description, description),
                AllowManagementOS = hostPort is not null,
                VlanId            = hostVlan is not null && L(hostVlan, "OperationMode") == 1 ? (int)L(hostVlan, "AccessVlanId") : 0,
            });
        }
        return result.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<NatNetwork> GetNatNetworks()
    {
        var addresses = Rows(Std, "SELECT InterfaceAlias, IPAddress FROM MSFT_NetIPAddress WHERE AddressFamily = 2")
            .Select(r => (S(r, "InterfaceAlias"), S(r, "IPAddress")))
            .ToList();
        return Rows(Std, "SELECT Name, InternalIPInterfaceAddressPrefix, Active FROM MSFT_NetNat")
            .Select(r => new NatNetwork
            {
                Name = S(r, "Name"),
                InternalIPInterfaceAddressPrefix = S(r, "InternalIPInterfaceAddressPrefix"),
                Active     = r.GetValueOrDefault("Active") is true,
                SwitchName = WmiMap.NatSwitch(S(r, "InternalIPInterfaceAddressPrefix"), addresses),
            })
            .ToList();
    }

    // ─── Network topology ───────────────────────────────────────────────────

    /// <summary>
    /// Every adapter of every VM with its switch, VLAN and IP. A resource InstanceID reads
    /// "Microsoft:&lt;VM&gt;\&lt;adapter&gt;\…": the second GUID ties the port, its VLAN and the guest IP to the adapter.
    /// </summary>
    public List<VmNetworkAdapter> GetVmNetworkAdapters()
    {
        var vms = Rows(V2, "SELECT Name, ElementName, EnabledState FROM Msvm_ComputerSystem WHERE Caption = 'Virtual Machine'")
            .ToDictionary(r => S(r, "Name").ToUpperInvariant(), r => (Name: S(r, "ElementName"), State: WmiMap.StateName(L(r, "EnabledState"))));
        var switchNames = Rows(V2, "SELECT Name, ElementName FROM Msvm_VirtualEthernetSwitch")
            .ToDictionary(r => S(r, "Name").ToUpperInvariant(), r => S(r, "ElementName"));

        static string AdapterKey(IReadOnlyList<string> guids) => guids.Count >= 2 ? $"{guids[0]}\\{guids[1]}" : "";
        var ports = Rows(V2, "SELECT InstanceID, HostResource, EnabledState FROM Msvm_EthernetPortAllocationSettingData")
            .GroupBy(r => AdapterKey(WmiMap.Guids(S(r, "InstanceID"))))
            .ToDictionary(g => g.Key, g => g.First());
        var vlans = Rows(V2, "SELECT InstanceID, OperationMode, AccessVlanId, TrunkVlanIdArray FROM Msvm_EthernetSwitchPortVlanSettingData")
            .GroupBy(r => AdapterKey(WmiMap.Guids(S(r, "InstanceID"))))
            .ToDictionary(g => g.Key, g => g.First());
        // Guest IPs: "Microsoft:GuestNetwork\<VM>\<adapter>"
        var ips = Rows(V2, "SELECT InstanceID, IPAddresses FROM Msvm_GuestNetworkAdapterConfiguration")
            .GroupBy(r => AdapterKey(WmiMap.Guids(S(r, "InstanceID"))))
            .ToDictionary(g => g.Key, g => WmiMap.FirstIpv4(g.SelectMany(r => Strings(r, "IPAddresses"))) ?? "");

        var result = new List<VmNetworkAdapter>();
        // Synthetic adapters, and the legacy (emulated) adapters of generation 1 VMs
        foreach (var nic in Rows(V2, "SELECT InstanceID, ElementName, Address FROM Msvm_SyntheticEthernetPortSettingData")
                     .Concat(SafeRows(V2, "SELECT InstanceID, ElementName, Address FROM Msvm_EmulatedEthernetPortSettingData")))
        {
            var guids = WmiMap.Guids(S(nic, "InstanceID"));
            var key = AdapterKey(guids);
            if (key.Length == 0 || !vms.TryGetValue(guids[0], out var vm)) continue;   // definitions and checkpoints
            var switchName = "";
            if (ports.TryGetValue(key, out var port) && L(port, "EnabledState") != 3
                && WmiMap.PathKey(Strings(port, "HostResource").FirstOrDefault(), "Name") is { } sw)
                switchName = switchNames.GetValueOrDefault(sw.ToUpperInvariant(), "");
            var vlan = vlans.TryGetValue(key, out var v)
                ? WmiMap.VlanText(L(v, "OperationMode"), L(v, "AccessVlanId"),
                                  (v.GetValueOrDefault("TrunkVlanIdArray") as Array)?.Cast<object>().Select(Convert.ToInt64))
                : "";
            result.Add(new VmNetworkAdapter(guids[0], vm.Name, vm.State, S(nic, "ElementName"),
                WmiMap.FormatMac(S(nic, "Address")), switchName, vlan, ips.GetValueOrDefault(key, "")));
        }
        return result.OrderBy(a => a.VmName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>IPv4 address of each host adapter, by interface alias ("vEthernet (LAB)" → "192.168.100.1").</summary>
    public Dictionary<string, string> GetHostAddresses()
        => Rows(Std, "SELECT InterfaceAlias, IPAddress FROM MSFT_NetIPAddress WHERE AddressFamily = 2")
            .Where(r => !S(r, "IPAddress").StartsWith("169.254."))
            .GroupBy(r => S(r, "InterfaceAlias"))
            .ToDictionary(g => g.Key, g => S(g.First(), "IPAddress"), StringComparer.OrdinalIgnoreCase);

    /// <summary>Physical adapters that are up: the ones an External switch can bind to.</summary>
    public List<NetworkAdapterInfo> GetPhysicalAdapters()
        => Rows(Std, "SELECT Name, InterfaceDescription, NetworkAddresses, PermanentAddress, InterfaceOperationalStatus, HardwareInterface FROM MSFT_NetAdapter")
            .Where(r => L(r, "InterfaceOperationalStatus") == 1 && r.GetValueOrDefault("HardwareInterface") is true)
            .Select(r => new NetworkAdapterInfo
            {
                Name        = S(r, "Name"),
                Description = S(r, "InterfaceDescription"),
                // The address in use (as Get-NetAdapter shows it), which differs from the factory one on some adapters
                MacAddress  = WmiMap.FormatMac(Strings(r, "NetworkAddresses").FirstOrDefault() is { Length: > 0 } current
                                               ? current : S(r, "PermanentAddress")),
                Status      = "Up",
            })
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Query of an optional class (Wi-Fi ports, net adapters): empty instead of failing.</summary>
    private List<Dictionary<string, object?>> SafeRows(string ns, string wql)
    {
        try { return Rows(ns, wql); }
        catch (ManagementException) { return []; }
    }
}
