using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVLab.Models;
using HVLab.Services;
using static HVLab.Services.LocalizationService;

namespace HVLab.ViewModels;

/// <summary>
/// One VM in its own window: live screen and details, power actions, credentials, checkpoints and settings.
/// The window keeps its own copy of the VM and refreshes it through WMI, so it stays live whatever page
/// the main window shows.
/// </summary>
public partial class VmDetailsViewModel(IHyperVService hvService, IDialogService dialogs) : PageViewModelBase
{
    private readonly IHyperVService _hvService = hvService;
    private readonly IDialogService _dialogs   = dialogs;

    /// <summary>The window's VM, updated in place by <see cref="RefreshAsync"/>.</summary>
    public VirtualMachine Vm { get; } = new();

    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = T("St_Ready");

    /// <summary>Raised when a refresh no longer finds the VM (deleted elsewhere): the window closes.</summary>
    public event EventHandler? VmRemoved;

    public void Initialize(VirtualMachine source)
    {
        Vm.Id = source.Id;
        Vm.Generation = source.Generation;
        Vm.UpdateFrom(source);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var fresh = (await _hvService.GetVirtualMachinesAsync()).FirstOrDefault(v => v.Id == Vm.Id);
            if (fresh is null) { VmRemoved?.Invoke(this, EventArgs.Empty); return; }
            if (!Vm.IsBusy) Vm.UpdateFrom(fresh);
            MarkLoaded();   // clears a refresh error once WMI answers again
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ─── Power actions ──────────────────────────────────────────────────────

    [RelayCommand] private Task StartAsync()   => PowerAsync(VmPowerAction.Start,    "Starting", "VM_Starting");
    [RelayCommand] private Task ShutdownAsync() => PowerAsync(VmPowerAction.Shutdown, "Stopping", "VM_Stopping");
    [RelayCommand] private Task TurnOffAsync() => PowerAsync(VmPowerAction.TurnOff,  "Stopping", "VM_TurningOff");
    [RelayCommand] private Task RestartAsync() => PowerAsync(VmPowerAction.Restart,  null,       "VM_Restarting");
    [RelayCommand] private Task ResetAsync()   => PowerAsync(VmPowerAction.Reset,    null,       "VM_Resetting");
    [RelayCommand] private Task SaveAsync()    => PowerAsync(VmPowerAction.Save,     "Saving",   "VM_Saving");
    [RelayCommand] private Task PauseAsync()   => PowerAsync(VmPowerAction.Pause,    "Pausing",  "VM_Pausing");
    [RelayCommand] private Task ResumeAsync()  => PowerAsync(VmPowerAction.Resume,   "Resuming", "VM_Resuming");

    /// <summary>One action at a time: transitional state shown at once, then a refresh.</summary>
    private async Task PowerAsync(VmPowerAction action, string? optimisticState, string statusKey)
    {
        if (Vm.IsBusy) return;
        ClearActionError();
        Vm.IsBusy = true;
        var previous = Vm.State;
        if (optimisticState is not null) Vm.State = optimisticState;
        Status = T(statusKey, Vm.Name);
        try
        {
            await _hvService.PowerAsync(Vm.Id, action);
            Status = T("VM_ActionOk", Vm.Name);
        }
        catch (Exception ex) { Vm.State = previous; ShowActionError(ex); }
        finally { Vm.IsBusy = false; }
        await RefreshAsync();
    }

    [RelayCommand]
    private void Connect()
    {
        try   { _hvService.ConnectToVMConsole(Vm.Target); }
        catch (Exception ex) { ShowActionError(ex, T("VM_ErrConsole")); }
    }

    /// <summary>Non-power action (keyboard, checkpoint…): status, error banner.</summary>
    private async Task RunAsync(string status, Func<Task> action)
    {
        ClearActionError();
        IsLoading = true;
        Status = status;
        try { await action(); Status = T("St_Ready"); }
        catch (Exception ex) { ShowActionError(ex); }
        finally { IsLoading = false; }
    }

    // ─── Screen and keyboard ────────────────────────────────────────────────

    [ObservableProperty] private VmThumbnail? thumbnail;

    /// <summary>Called by the window's timer while the overview shows a running VM.</summary>
    public async Task RefreshThumbnailAsync(int maxWidth, int maxHeight)
    {
        if (!Vm.IsRunning) { Thumbnail = null; return; }
        try { Thumbnail = await _hvService.GetThumbnailAsync(Vm.Id, maxWidth, maxHeight); }
        catch { Thumbnail = null; }   // the screen is a convenience: never an error banner
    }

    /// <summary>Guest keyboard layout used to type the password (the VM's answer-file language).</summary>
    [ObservableProperty] private string typingLanguage = LocalizationService.Instance.CurrentLanguage == "en" ? "en-US" : "fr-FR";
    public IReadOnlyList<string> TypingLanguages => LabCatalog.Languages;

    [RelayCommand]
    private async Task TypePasswordAsync()
    {
        if (!Vm.Credentials.HasPassword) { Status = T("VM_NoPassword"); return; }
        await RunAsync(T("VM_TypingPassword", Vm.Name),
            () => Queued(() => _hvService.TypeTextAsync(Vm.Id, Vm.Credentials.Password, TypingLanguage, true)));
    }

    [RelayCommand]
    private Task SendCtrlAltDelAsync() => RunAsync(T("VM_CtrlAltDel", Vm.Name), () => Queued(() => _hvService.SendCtrlAltDelAsync(Vm.Id)));

    /// <summary>
    /// Everything typed into the VM (password, clipboard, Ctrl+Alt+Del, console keys and mouse) goes through one
    /// queue, so a password being typed never interleaves with console keys. Errors reach the caller.
    /// </summary>
    private async Task Queued(Func<Task> call)
    {
        await _input.WaitAsync();
        try { await call(); }
        finally { _input.Release(); }
    }

    // ─── Integrated console: live screen at the guest resolution, zoom, keyboard and mouse ──

    [ObservableProperty] private VmThumbnail? consoleFrame;
    /// <summary>Index in <see cref="ConsoleInput.ZoomLevels"/> (0 = fit to the window).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Zoom), nameof(IsFit))] private int zoomIndex;

    public int  Zoom  => ConsoleInput.ZoomLevels[Math.Clamp(ZoomIndex, 0, ConsoleInput.ZoomLevels.Count - 1)];
    public bool IsFit => Zoom == 0;
    public IReadOnlyList<string> ZoomOptions { get; } =
        ConsoleInput.ZoomLevels.Select(z => z == 0 ? T("CON_ZoomFit") : $"{z} %").ToList();

    /// <summary>Ctrl + wheel on the screen: one zoom level up or down.</summary>
    public void StepZoom(bool up)
    {
        var levels = ConsoleInput.ZoomLevels;
        ZoomIndex = levels.ToList().IndexOf(ConsoleInput.StepZoom(Zoom, up));
    }

    /// <summary>Next frame of the console (called in a loop by the window while the Console tab is shown).</summary>
    public async Task RefreshConsoleAsync()
    {
        if (!Vm.IsRunning) { ConsoleFrame = null; return; }
        // Native resolution (up to 2560×1600), so a frame pixel is a guest pixel for the mouse
        try { ConsoleFrame = await _hvService.GetThumbnailAsync(Vm.Id, 2560, 1600); }
        catch { ConsoleFrame = null; }
    }

    // Input goes out in order, one WMI call at a time; mouse moves are coalesced (only the last position is sent)
    private readonly SemaphoreSlim _input = new(1, 1);
    private (int X, int Y)? _pendingMove;
    private bool _moving;

    private async Task SendAsync(Func<Task> call)
    {
        await _input.WaitAsync();
        try { await call(); }
        catch (Exception ex) { Status = T("CON_InputError", ex.Message); }   // no banner: input keeps flowing
        finally { _input.Release(); }
    }

    /// <summary>A physical key pressed or released in the console (scancode of the host keyboard).</summary>
    public Task SendKeyAsync(uint scanCode, bool extended, bool keyUp)
    {
        var codes = ConsoleInput.Scancodes(scanCode, extended, keyUp);
        return codes.Length == 0 ? Task.CompletedTask : SendAsync(() => _hvService.SendScancodesAsync(Vm.Id, codes));
    }

    public void MoveMouse(int x, int y)
    {
        _pendingMove = (x, y);
        if (_moving) return;
        _moving = true;
        _ = PumpMovesAsync();
    }

    private async Task PumpMovesAsync()
    {
        try
        {
            while (_pendingMove is { } p)
            {
                _pendingMove = null;
                await SendAsync(() => _hvService.MouseMoveAsync(Vm.Id, p.X, p.Y));
            }
        }
        finally { _moving = false; }
    }

    /// <summary>Button 1 = left, 2 = right, 3 = middle.</summary>
    public Task MouseButtonAsync(int button, bool down) => SendAsync(async () =>
    {
        // The click lands where the pointer is now: a move still waiting goes out first
        if (_pendingMove is { } p) { _pendingMove = null; await _hvService.MouseMoveAsync(Vm.Id, p.X, p.Y); }
        await _hvService.MouseButtonAsync(Vm.Id, button, down);
    });

    public Task MouseWheelAsync(int delta) => SendAsync(() => _hvService.MouseWheelAsync(Vm.Id, delta));

    /// <summary>Types text (the clipboard) with the guest keyboard layout chosen for the password.</summary>
    public Task TypeTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text)) { Status = T("CON_ClipboardEmpty"); return Task.CompletedTask; }
        return RunAsync(T("CON_TypingClipboard"), () => Queued(() => _hvService.TypeTextAsync(Vm.Id, text, TypingLanguage, false)));
    }

    // ─── Checkpoints ────────────────────────────────────────────────────────

    public ObservableCollection<CheckpointRow> Checkpoints { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCheckpointSelection))] private CheckpointRow? selectedCheckpoint;
    public bool HasCheckpointSelection => SelectedCheckpoint is { IsNow: false };
    [ObservableProperty] private bool hasNoCheckpoints;

    public async Task LoadCheckpointsAsync()
    {
        try
        {
            var rows = await _hvService.GetCheckpointsAsync(Vm.Id);
            Checkpoints.Clear();
            foreach (var r in rows) Checkpoints.Add(r);
            HasNoCheckpoints = rows.Count == 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    [RelayCommand]
    private async Task CreateCheckpointAsync()
    {
        var name = await _dialogs.PromptAsync(T("CP_CreateTitle"), T("CP_Name"), $"{Vm.Name} - {DateTime.Now:yyyy-MM-dd HH:mm}");
        if (name is null) return;
        await RunAsync(T("CP_Creating", name), () => _hvService.CreateCheckpointAsync(Vm.Target, name));
        await LoadCheckpointsAsync();
    }

    [RelayCommand]
    private async Task ApplyCheckpointAsync()
    {
        if (SelectedCheckpoint is not { IsNow: false } cp) return;
        if (!await _dialogs.ConfirmAsync(T("CP_ApplyTitle"), T("CP_ApplyConfirm", cp.Name, Vm.Name), T("CP_Apply"))) return;
        await RunAsync(T("CP_Applying", cp.Name), () => _hvService.ApplyCheckpointAsync(Vm.Target, cp.Id));
        await LoadCheckpointsAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveCheckpointAsync()
    {
        if (SelectedCheckpoint is not { IsNow: false } cp) return;
        var hasChildren = Checkpoints.SkipWhile(r => r != cp).Skip(1).TakeWhile(r => r.Depth > cp.Depth).Any(r => !r.IsNow);
        var (confirmed, withChildren) = await _dialogs.ConfirmWithOptionAsync(T("CP_DeleteTitle"), T("CP_DeleteConfirm", cp.Name),
            T("VM_DeleteConfirm"), hasChildren ? T("CP_DeleteSubtree") : null, false);
        if (!confirmed) return;
        await RunAsync(T("CP_Deleting", cp.Name), () => _hvService.RemoveCheckpointAsync(Vm.Target, cp.Id, withChildren));
        await LoadCheckpointsAsync();
    }

    [RelayCommand]
    private async Task RenameCheckpointAsync()
    {
        if (SelectedCheckpoint is not { IsNow: false } cp) return;
        var name = await _dialogs.PromptAsync(T("CP_RenameTitle"), T("CP_Name"), cp.Name);
        if (name is null || name == cp.Name) return;
        await RunAsync(T("CP_Renaming", name), () => _hvService.RenameCheckpointAsync(Vm.Target, cp.Id, name));
        await LoadCheckpointsAsync();
    }

    // ─── Settings (inline form of the Settings tab) ─────────────────────────

    [ObservableProperty] private VmSettings? editingSettings;
    private VmSettings? _settingsBefore;
    public ObservableCollection<string> SwitchNames { get; } = [];
    [ObservableProperty] private string settingsError = "";

    public async Task LoadSettingsAsync()
    {
        IsLoading = true;
        SettingsError = "";
        try
        {
            var settingsTask = _hvService.GetVmSettingsAsync(Vm.Target);
            var switchTask   = _hvService.GetVirtualSwitchesAsync();
            await Task.WhenAll(settingsTask, switchTask);
            SwitchNames.Clear();
            foreach (var s in switchTask.Result) SwitchNames.Add(s.Name);
            _settingsBefore = settingsTask.Result;
            EditingSettings = _settingsBefore.Clone();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        if (EditingSettings is not { } after || _settingsBefore is not { } before) return;
        if (after.Validate() is { } key) { SettingsError = T(key); return; }
        SettingsError = "";
        await RunAsync(T("VM_SavingSettings", Vm.Name), () => _hvService.SetVmSettingsAsync(Vm.Target, before, after));
        if (!HasError) await LoadSettingsAsync();   // show what Hyper-V really applied
        await RefreshAsync();
    }

    [RelayCommand]
    private void ResetSettings()
    {
        if (_settingsBefore is { } before) EditingSettings = before.Clone();
        SettingsError = "";
    }
}
