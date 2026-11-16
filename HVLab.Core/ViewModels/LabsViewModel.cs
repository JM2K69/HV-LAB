using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

/// <summary>A blueprint file of the Blueprints folder, as listed on the Labs page.</summary>
public sealed record LabFile(string Path, string Name, int VmCount, DateTime Modified, bool IsValid)
{
    public string Detail => IsValid ? T("LABS_FileDetail", VmCount, Modified) : T("LABS_FileInvalid");
}

/// <summary>One VM of the lab being edited (a row of the editor).</summary>
public sealed partial class LabVmRow : ObservableObject
{
    [ObservableProperty] private string name = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLinux), nameof(IsWindows))] private string @base = "";
    [ObservableProperty] private double memoryMB = 2048;
    [ObservableProperty] private double cpu = 2;
    [ObservableProperty] private string ip = "";
    [ObservableProperty] private string dns = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanJoin))] private bool domainController;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanBeDc))] private bool join;
    [ObservableProperty] private string user = "labadmin";
    /// <summary>Comma-separated, as typed; the blueprint keeps a clean list.</summary>
    [ObservableProperty] private string tags = "";
    /// <summary>Linux: first-boot commands, one per line.</summary>
    [ObservableProperty] private string commands = "";

    /// <summary>Linux base (cloud-init: a sudo user) or Windows (unattend: can join the domain).</summary>
    public bool IsLinux   => Base.StartsWith("Linux", StringComparison.OrdinalIgnoreCase);
    public bool IsWindows => !IsLinux;
    /// <summary>The domain controller creates the domain, a member joins it: never both.</summary>
    public bool CanJoin   => !DomainController;
    public bool CanBeDc   => !Join;

    public static LabVmRow From(LabVm vm) => new()
    {
        Name = vm.Name, Base = vm.Base, MemoryMB = vm.MemoryMB, Cpu = vm.Cpu,
        Ip = vm.Ip ?? "", Dns = vm.Dns ?? "", Join = vm.Join, DomainController = vm.DomainController,
        User = vm.User ?? "labadmin", Tags = string.Join(", ", vm.Tags ?? []),
        Commands = string.Join(Environment.NewLine, vm.Commands ?? []),
    };

    public LabVm ToVm() => new()
    {
        Name = Name.Trim(), Base = Base.Trim(), MemoryMB = (long)MemoryMB, Cpu = (int)Cpu,
        Ip = Ip.Trim() is { Length: > 0 } ip ? ip : null,
        Dns = Dns.Trim() is { Length: > 0 } dns ? dns : null,
        Join = IsWindows && Join,
        DomainController = IsWindows && DomainController,
        User = IsLinux && User.Trim().Length > 0 ? User.Trim() : null,
        Tags = VmNotes.ParseTags(Tags) is { Count: > 0 } list ? list.ToList() : null,
        Commands = IsLinux && CloudInitGenerator.ParseCommands(Commands) is { Count: > 0 } run ? run.ToList() : null,
    };
}

/// <summary>A line of the deployment plan, as shown on the page.</summary>
public sealed record LabPlanRow(string Action, string Kind, string Name, string Detail);

/// <summary>
/// The Labs page: the blueprints of the Blueprints folder, a graphical editor of one lab (switch, NAT, domain, VMs),
/// its deployment plan, and its deployment. The JSON file stays the storage, never something to edit by hand.
/// </summary>
public partial class LabsViewModel(LabBlueprintService labs, IHyperVService hv, IVhdxService vhdx, IDialogService dialogs)
    : PageViewModelBase
{
    public ObservableCollection<LabFile> Files { get; } = [];
    public ObservableCollection<LabVmRow> Vms { get; } = [];
    public ObservableCollection<LabPlanRow> Plan { get; } = [];
    public ObservableCollection<string> SwitchNames { get; } = [];
    /// <summary>OS identifiers of the host's bases, as patterns (WindowsServer2025Standard*): newest version wins.</summary>
    public ObservableCollection<string> BasePatterns { get; } = [];

    public IReadOnlyList<string> Languages => LabCatalog.Languages;
    public IReadOnlyList<string> TimeZones => LabCatalog.TimeZones;

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = T("St_Ready");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSuccess))] private string successMessage = "";
    public bool HasSuccess => SuccessMessage.Length > 0;

    // ── The lab being edited (null path = new, not saved yet)
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEditor), nameof(HasNoEditor))] private bool editorOpen;
    public bool HasEditor   => EditorOpen;
    public bool HasNoEditor => !EditorOpen;
    private string? _path;

    [ObservableProperty] private LabFile? selectedFile;
    [ObservableProperty] private string labName = "";
    [ObservableProperty] private string switchName = "";
    [ObservableProperty] private bool   useNat = true;
    [ObservableProperty] private string natGateway = "192.168.100.1";
    [ObservableProperty] private double natPrefix = 24;
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string language = "fr-FR";
    [ObservableProperty] private string timeZone = "Romance Standard Time";
    [ObservableProperty] private bool   useDomain;
    [ObservableProperty] private string domainName = "lab.local";
    [ObservableProperty] private string domainUser = "Administrator";
    [ObservableProperty] private string domainOu = "";
    [ObservableProperty] private bool   isDirty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPlan))] private bool planChecked;
    public bool HasPlan => PlanChecked;

    private bool _loadingEditor;
    private bool _changingSelection;

    private static readonly HashSet<string> EditorFields =
    [
        nameof(LabName), nameof(SwitchName), nameof(UseNat), nameof(NatGateway), nameof(NatPrefix), nameof(Password),
        nameof(Language), nameof(TimeZone), nameof(UseDomain), nameof(DomainName), nameof(DomainUser), nameof(DomainOu),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } p && EditorFields.Contains(p)) Touch();
    }

    /// <summary>Any edit: the file has unsaved changes, the plan is out of date, the JSON preview follows.</summary>
    private void Touch()
    {
        if (_loadingEditor) return;
        IsDirty = true;
        PlanChecked = false;
        Plan.Clear();
        OnPropertyChanged(nameof(JsonPreview));
    }

    /// <summary>The JSON the form generates (what Save writes), the password masked.</summary>
    public string JsonPreview
    {
        get
        {
            if (!EditorOpen) return "";
            var lab = ToBlueprint();
            if (lab.Password.Length > 0) lab.Password = "********";
            return lab.ToJson();
        }
    }

    public bool HasFiles => Files.Count > 0;
    public bool HasPath  => _path is not null;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Touch();

    private void AddRow(LabVmRow row)
    {
        row.PropertyChanged += Row_PropertyChanged;
        Vms.Add(row);
    }

    // ─── Loading ────────────────────────────────────────────────────────────

    /// <summary>Lists the blueprints, the switches and the bases; opens the file another page asked for.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            ReadFiles();
            var switches = await hv.GetVirtualSwitchesAsync();
            var bases    = await vhdx.GetBaseVhdxListAsync(labs.BaseFolder());
            Sync(SwitchNames, switches.Select(s => s.Name).Order());
            Sync(BasePatterns, bases.Where(b => b.IsBuilt && b.OsIdentifier.Length > 0)
                                    .Select(b => b.OsIdentifier + "*").Distinct(StringComparer.OrdinalIgnoreCase).Order());
            MarkLoaded();
            if (labs.RequestedFile is { } requested)
            {
                labs.RequestedFile = null;
                if (Files.FirstOrDefault(f => string.Equals(f.Path, requested, StringComparison.OrdinalIgnoreCase)) is { } file)
                    await OpenAsync(file);
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsLoading = false; }
    }

    private static void Sync(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var v in values) target.Add(v);
    }

    private void ReadFiles()
    {
        var folder = labs.BlueprintsFolder();
        var files = !Directory.Exists(folder) ? [] : Directory.GetFiles(folder, "*.json").Select(path =>
        {
            var modified = File.GetLastWriteTime(path);
            try
            {
                var lab = LabBlueprint.Parse(File.ReadAllText(path));
                return new LabFile(path, Path.GetFileNameWithoutExtension(path), lab.Vms.Count, modified, true);
            }
            catch (Exception) { return new LabFile(path, Path.GetFileNameWithoutExtension(path), 0, modified, false); }
        }).OrderByDescending(f => f.Modified).ToList();

        _changingSelection = true;
        Files.Clear();
        foreach (var f in files) Files.Add(f);
        SelectedFile = _path is null ? null : Files.FirstOrDefault(f => string.Equals(f.Path, _path, StringComparison.OrdinalIgnoreCase));
        _changingSelection = false;
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(HasPath));
    }

    partial void OnSelectedFileChanged(LabFile? value)
    {
        if (_changingSelection || value is null || string.Equals(value.Path, _path, StringComparison.OrdinalIgnoreCase)) return;
        _ = OpenAsync(value);
    }

    /// <summary>Unsaved edits are only dropped once the user agreed.</summary>
    private async Task<bool> CanLeaveEditorAsync()
        => !IsDirty || await dialogs.ConfirmAsync(T("LABS_DiscardTitle"), T("LABS_DiscardText", LabName), T("LABS_Discard"));

    public async Task OpenAsync(LabFile file)
    {
        if (!await CanLeaveEditorAsync())
        {
            _changingSelection = true;
            SelectedFile = _path is null ? null : Files.FirstOrDefault(f => f.Path == _path);
            _changingSelection = false;
            return;
        }
        ClearActionError();
        try { Edit(LabBlueprint.Parse(await File.ReadAllTextAsync(file.Path)), file.Path); }
        catch (Exception ex) { ShowActionError(T("LAB_ErrRead", ex.Message)); }
    }

    /// <summary>Puts a blueprint in the editor.</summary>
    private void Edit(LabBlueprint lab, string? path)
    {
        _loadingEditor = true;
        _path = path;
        LabName = lab.Name;
        SwitchName = lab.Switch;
        UseNat = lab.Nat is not null;
        NatGateway = lab.Nat?.Gateway ?? "192.168.100.1";
        NatPrefix = lab.Nat?.PrefixLength ?? 24;
        Password = lab.Password;
        Language = lab.Language;
        TimeZone = lab.TimeZone;
        UseDomain = lab.Domain is not null;
        DomainName = lab.Domain?.Name ?? "lab.local";
        DomainUser = lab.Domain?.User ?? "Administrator";
        DomainOu = lab.Domain?.Ou ?? "";
        foreach (var row in Vms) row.PropertyChanged -= Row_PropertyChanged;
        Vms.Clear();
        foreach (var vm in lab.Vms) AddRow(LabVmRow.From(vm));
        Plan.Clear();
        PlanChecked = false;
        IsDirty = path is null;
        EditorOpen = true;
        _loadingEditor = false;
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(HasPath));
        _changingSelection = true;
        SelectedFile = path is null ? null : Files.FirstOrDefault(f => f.Path == path);
        _changingSelection = false;
    }

    /// <summary>The editor as a blueprint (what is saved and deployed).</summary>
    public LabBlueprint ToBlueprint() => new()
    {
        Name = LabName.Trim(),
        Switch = SwitchName.Trim(),
        Nat = UseNat ? new LabNat { Gateway = NatGateway.Trim(), PrefixLength = (int)NatPrefix } : null,
        Password = Password,
        Language = Language,
        TimeZone = TimeZone,
        Domain = UseDomain ? new LabDomain { Name = DomainName.Trim(), User = DomainUser.Trim(), Ou = DomainOu.Trim() is { Length: > 0 } ou ? ou : null } : null,
        Vms = Vms.Select(r => r.ToVm()).ToList(),
    };

    // ─── Commands ───────────────────────────────────────────────────────────

    /// <summary>A new lab from the commented template (the host's newest bases), not saved yet.</summary>
    [RelayCommand]
    private async Task NewLabAsync()
    {
        if (!await CanLeaveEditorAsync()) return;
        ClearActionError();
        SuccessMessage = "";
        try { Edit(LabBlueprint.Parse(await labs.NewTemplateAsync()), null); }
        catch (Exception ex) { ShowActionError(ex); }
    }

    /// <summary>Rows added or moved also change the JSON (the row edits go through <see cref="Row_PropertyChanged"/>).</summary>
    [RelayCommand]
    private void AddVm()
    {
        var number = Vms.Count + 1;
        var name = $"{(LabName.Trim() is { Length: > 0 } n ? n : "LAB")}-VM{number:D2}";
        AddRow(new LabVmRow { Name = name, Base = Vms.LastOrDefault()?.Base ?? BasePatterns.FirstOrDefault() ?? "" });
        Touch();
    }

    [RelayCommand]
    private void RemoveVm(LabVmRow row)
    {
        row.PropertyChanged -= Row_PropertyChanged;
        Vms.Remove(row);
        Touch();
    }

    /// <summary>VMs are created in this order: the domain controller first.</summary>
    [RelayCommand]
    private void MoveUp(LabVmRow row)
    {
        var i = Vms.IndexOf(row);
        if (i <= 0) return;
        Vms.Move(i, i - 1);
        Touch();
    }

    [RelayCommand]
    private void MoveDown(LabVmRow row)
    {
        var i = Vms.IndexOf(row);
        if (i < 0 || i >= Vms.Count - 1) return;
        Vms.Move(i, i + 1);
        Touch();
    }

    /// <summary>Validates and writes the blueprint (a new lab gets a free file name after its name).</summary>
    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        ClearActionError();
        var lab = ToBlueprint();
        if (lab.Validate(requirePassword: false) is { } invalid) { ShowActionError(invalid); return false; }
        try
        {
            var path = _path ?? labs.FreePath(lab.Name);
            await File.WriteAllTextAsync(path, lab.ToJson());
            _path = path;
            IsDirty = false;
            Status = T("LABS_Saved", path);
            ReadFiles();
            return true;
        }
        catch (Exception ex) { ShowActionError(ex); return false; }
    }

    /// <summary>What a deployment would do: created, already there, or impossible (no matching base).</summary>
    [RelayCommand]
    private async Task CheckPlanAsync()
    {
        ClearActionError();
        var lab = ToBlueprint();
        if (lab.Validate(requirePassword: false) is { } invalid) { ShowActionError(invalid); return; }
        IsLoading = true;
        try { await ShowPlanAsync(lab); }
        catch (Exception ex) { ShowActionError(ex); }
        finally { IsLoading = false; }
    }

    private async Task<IReadOnlyList<LabStep>> ShowPlanAsync(LabBlueprint lab)
    {
        var steps = await labs.PlanAsync(lab);
        Plan.Clear();
        foreach (var s in steps)
            Plan.Add(new LabPlanRow(s.Action, s.Kind switch
                                    {
                                        "switch" => T("LAB_Kind_switch"), "nat" => T("LAB_Kind_nat"),
                                        "domain" => T("LAB_Kind_domain"), _ => T("LAB_Kind_vm"),
                                    },
                                    s.Name, s.Detail));
        PlanChecked = true;
        return steps;
    }

    /// <summary>Saves, shows the plan, and deploys what is missing once confirmed.</summary>
    [RelayCommand]
    private async Task DeployAsync()
    {
        ClearActionError();
        SuccessMessage = "";
        var lab = ToBlueprint();
        if (lab.Validate() is { } invalid) { ShowActionError(invalid); return; }
        if (!await SaveAsync()) return;
        IsLoading = true;
        try
        {
            var steps = await ShowPlanAsync(lab);
            if (steps.Any(s => s.Action == LabBlueprintService.Missing)) { ShowActionError(T("LAB_CannotDeploy")); return; }
            if (!steps.Any(s => s.Action == LabBlueprintService.Create)) { SuccessMessage = T("LAB_NothingToDo", lab.Name); return; }
            var created = steps.Count(s => s.Action == LabBlueprintService.Create);
            if (!await dialogs.ConfirmAsync(T("LAB_DeployTitle", lab.Name), T("LABS_DeployText", created), T("LAB_Deploy"))) return;
            await labs.DeployAsync(lab, new Progress<string>(line => Status = line));
            Status = T("St_Ready");
            SuccessMessage = T("LAB_Deployed", lab.Name);
            await ShowPlanAsync(lab);
        }
        catch (Exception ex) { Status = T("St_Ready"); ShowActionError(ex); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DeleteLabAsync()
    {
        if (_path is null) { EditorOpen = false; IsDirty = false; return; }
        if (!await dialogs.ConfirmAsync(T("LABS_DeleteTitle"), T("LABS_DeleteText", Path.GetFileName(_path)), T("Btn_Delete"))) return;
        try
        {
            File.Delete(_path);
            _path = null;
            IsDirty = false;
            EditorOpen = false;
            ReadFiles();
        }
        catch (Exception ex) { ShowActionError(ex); }
    }

    [RelayCommand]
    private void DismissSuccess() => SuccessMessage = "";

    /// <summary>Folder of the blueprints (the page opens it in Explorer).</summary>
    public string BlueprintsFolder => labs.BlueprintsFolder();
    /// <summary>The file in the editor, for "Open the JSON" (null while a new lab is not saved).</summary>
    public string? CurrentPath => _path;
}
