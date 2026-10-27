using System.Diagnostics;
using System.Text;

namespace HVLab.Services;

/// <summary>
/// Error raised when a PowerShell script fails: <see cref="Exception.Message"/> is the
/// short, user-facing message; <see cref="Details"/> holds the full error output.
/// </summary>
public sealed class PowerShellException(string message, string details, int exitCode)
    : Exception(message)
{
    public string Details  { get; } = details;
    public int    ExitCode { get; } = exitCode;
}

/// <summary>
/// Runs scripts with Windows PowerShell 5.1 through a small pool of <b>warm</b> powershell.exe
/// workers: each worker starts once, imports the Hyper-V module once, then executes scripts sent
/// over stdin. This saves the ~600 ms of process start + module import of every call.
/// Scripts are never written to %TEMP% (a non-elevated process could swap them before the elevated run).
/// </summary>
public sealed class PowerShellRunner : IPowerShellRunner, IDisposable
{
    /// <summary>Maximum number of scripts running at the same time (= number of workers).</summary>
    public const int MaxWorkers = 3;

    /// <summary>After this much inactivity, idle workers beyond the first one are closed (saves ~110 MB each).</summary>
    public static readonly TimeSpan DefaultIdleTrimDelay = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim                    _slots = new(MaxWorkers, MaxWorkers);
    // Idle workers, guarded by _idleLock. A list (not a bag) so the most recently used worker is
    // always reused: regular polling then keeps a single worker busy and the others can expire.
    private readonly List<PowerShellWorker> _idle     = [];
    private readonly Lock                   _idleLock = new();
    private readonly TimeSpan _idleTrimDelay;
    private readonly Timer    _trimTimer;
    private volatile bool _disposed;

    public PowerShellRunner() : this(DefaultIdleTrimDelay) { }

    /// <param name="idleTrimDelay">Inactivity after which only one warm worker is kept.</param>
    internal PowerShellRunner(TimeSpan idleTrimDelay)
    {
        _idleTrimDelay = idleTrimDelay;
        var period = TimeSpan.FromMilliseconds(Math.Clamp(idleTrimDelay.TotalMilliseconds / 4, 50, 30_000));
        _trimTimer = new Timer(_ => TrimIdleWorkers(), null, period, period);
    }

    /// <summary>Number of warm workers currently waiting for a script (for tests and diagnostics).</summary>
    internal int IdleWorkerCount { get { lock (_idleLock) return _idle.Count; } }

    /// <summary>Returns a worker to the pool; false when it must be disposed instead.</summary>
    private bool TryReturnIdle(PowerShellWorker w)
    {
        lock (_idleLock)
        {
            if (_disposed || !w.IsAlive || _idle.Count >= MaxWorkers) return false;
            _idle.Add(w);
            return true;
        }
    }

    // Closes idle workers unused for _idleTrimDelay, always keeping the most recently used one warm
    // (per worker, so the 5 s VM polling, which only needs one worker, does not keep all three alive)
    private void TrimIdleWorkers()
    {
        var toDispose = new List<PowerShellWorker>();
        lock (_idleLock)
        {
            if (_disposed || _idle.Count <= 1) return;
            var keep = _idle.MaxBy(w => w.LastUsedTicks)!;
            foreach (var w in _idle)
                if (w != keep && (!w.IsAlive || Stopwatch.GetElapsedTime(w.LastUsedTicks) >= _idleTrimDelay))
                    toDispose.Add(w);
            _idle.RemoveAll(toDispose.Contains);
        }
        foreach (var w in toDispose) w.Dispose();   // outside the lock: Dispose waits for the process
    }

    /// <summary>
    /// Starts <paramref name="count"/> workers in the background so the first calls (splash screen)
    /// do not pay the start-up cost.
    /// </summary>
    public void Warmup(int count = 2)
    {
        for (int i = 0; i < Math.Min(count, MaxWorkers); i++)
            _ = Task.Run(() =>
            {
                try
                {
                    var w = PowerShellWorker.Start();
                    if (!TryReturnIdle(w)) w.Dispose();
                }
                catch (Exception ex) { CrashLogger.Log("PowerShellRunner.Warmup", ex); }
            });
    }

    /// <summary>
    /// Runs <paramref name="script"/> and returns its output.
    /// Each output line is forwarded to <paramref name="progress"/> as it arrives.
    /// On cancellation or timeout the worker is killed (and replaced on the next call) and
    /// <see cref="OperationCanceledException"/> / <see cref="TimeoutException"/> is thrown.
    /// </summary>
    public async Task<string> RunAsync(
        string script,
        IProgress<string>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var linked     = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        await _slots.WaitAsync(linked.Token);
        PowerShellWorker? worker = null;
        try
        {
            worker = TakeIdleWorker() ?? await Task.Run(PowerShellWorker.Start, CancellationToken.None);
            var result = await worker.RunAsync(script, progress, linked.Token);

            if (timeoutCts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
                throw new TimeoutException(LocalizationService.T("Err_Timeout", timeout!.Value.TotalSeconds.ToString("0")));
            ct.ThrowIfCancellationRequested();

            if (result.ExitCode != 0)
            {
                var details  = result.Errors.Length > 0 ? result.Errors : result.Output.Trim();
                // Scripts throw "##Key|args" for known errors: shown in the UI language
                var lastLine = details.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .LastOrDefault();
                var message  = lastLine is null
                    ? LocalizationService.T("Err_ExitCode", result.ExitCode)
                    : LocalizationService.TranslateProgress(lastLine);
                throw new PowerShellException(message, details, result.ExitCode);
            }
            return result.Output;
        }
        finally
        {
            // A worker that was killed (cancel/timeout) or that exited (script called "exit") is dropped
            if (worker is not null && !TryReturnIdle(worker))
                worker.Dispose();
            _slots.Release();
        }
    }

    /// <summary>Takes the most recently used live worker, or null when none is idle.</summary>
    private PowerShellWorker? TakeIdleWorker()
    {
        var dead = new List<PowerShellWorker>();
        PowerShellWorker? best = null;
        lock (_idleLock)
        {
            dead.AddRange(_idle.Where(w => !w.IsAlive));
            _idle.RemoveAll(dead.Contains);
            best = _idle.MaxBy(w => w.LastUsedTicks);
            if (best is not null) _idle.Remove(best);
        }
        foreach (var w in dead) w.Dispose();
        return best;
    }

    public void Dispose()
    {
        _disposed = true;
        _trimTimer.Dispose();
        List<PowerShellWorker> all;
        lock (_idleLock) { all = [.. _idle]; _idle.Clear(); }
        foreach (var w in all) w.Dispose();
    }

    /// <summary>
    /// Escapes a value for use inside a single-quoted PowerShell string: doubles the ASCII
    /// apostrophe and the typographic quotes U+2018–U+201B, which PowerShell also treats as quotes.
    /// </summary>
    public static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Outcome of one script run by a worker.</summary>
internal readonly record struct ScriptResult(string Output, string Errors, int ExitCode);

/// <summary>
/// One long-lived powershell.exe running a request loop. Protocol (UTF-8 over stdin/stdout):
/// request = "&lt;length in chars&gt;\n&lt;script&gt;"; the worker answers with the script output lines,
/// error lines prefixed by <see cref="ErrPrefix"/>, then "<see cref="EndPrefix"/>&lt;exit code&gt;".
/// </summary>
internal sealed class PowerShellWorker : IDisposable
{
    // Fixed path to Windows PowerShell 5.1 (System32) — intentionally not user-configurable,
    // since scripts run elevated. Hyper-V cmdlets are loaded from the Windows PowerShell module path.
    private static readonly string PowerShellExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     @"WindowsPowerShell\v1.0\powershell.exe");

    internal const string ReadyLine = "##HVLAB-READY";
    internal const string ErrPrefix = "##HVLAB-ERR ";
    internal const string EndPrefix = "##HVLAB-END ";

    // Each script runs in its own child scope (no variable leaks between requests); error records
    // are written as plain one-line messages (no CLIXML / "At line:char" noise).
    private const string Bootstrap = """
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $hvlIn  = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding $false))
        $hvlOut = [Console]::Out
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        $hvlOut.WriteLine('##HVLAB-READY'); $hvlOut.Flush()
        while ($true) {
            $hvlLen = $hvlIn.ReadLine()
            if ($null -eq $hvlLen) { break }
            $hvlBuf = New-Object char[] ([int]$hvlLen); $hvlRead = 0
            while ($hvlRead -lt $hvlBuf.Length) {
                $hvlN = $hvlIn.Read($hvlBuf, $hvlRead, $hvlBuf.Length - $hvlRead)
                if ($hvlN -le 0) { break }
                $hvlRead += $hvlN
            }
            $hvlCode = 0
            try {
                & ([scriptblock]::Create((New-Object string (,$hvlBuf)))) 2>&1 | ForEach-Object {
                    if ($_ -is [System.Management.Automation.ErrorRecord]) { $hvlOut.WriteLine('##HVLAB-ERR ' + $_.Exception.Message) }
                    else { $_ | Out-String -Stream -Width 32766 | ForEach-Object { $hvlOut.WriteLine($_) } }
                    $hvlOut.Flush()
                }
            } catch {
                $hvlOut.WriteLine('##HVLAB-ERR ' + $_.Exception.Message)
                $hvlCode = 1
            }
            $hvlOut.WriteLine('##HVLAB-END ' + $hvlCode); $hvlOut.Flush()
        }
        """;

    private static readonly string EncodedBootstrap =
        Convert.ToBase64String(Encoding.Unicode.GetBytes(Bootstrap));

    private readonly Process _proc;

    private PowerShellWorker(Process proc) => _proc = proc;

    /// <summary>
    /// <see cref="Stopwatch.GetTimestamp"/> at the end of the last script (or at start-up). High resolution on
    /// purpose: Environment.TickCount64 (~15 ms steps) made workers used in a burst look equally recent.
    /// </summary>
    public long LastUsedTicks { get; private set; } = Stopwatch.GetTimestamp();

    public bool IsAlive
    {
        get { try { return !_proc.HasExited; } catch { return false; } }
    }

    /// <summary>Starts powershell.exe and waits until Hyper-V is imported (blocking: call from a worker thread).</summary>
    public static PowerShellWorker Start()
    {
        var psi = new ProcessStartInfo
        {
            FileName               = PowerShellExe,
            Arguments              = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand {EncodedBootstrap}",
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardInputEncoding  = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException(LocalizationService.Instance["Err_PsStart"]);

        // Anything written outside the protocol (native tools writing to stderr directly) is discarded
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginErrorReadLine();

        while (proc.StandardOutput.ReadLine() is { } line)
            if (line == ReadyLine) return new PowerShellWorker(proc);

        var code = proc.HasExited ? proc.ExitCode : -1;
        proc.Dispose();
        throw new InvalidOperationException(LocalizationService.T("Err_ExitCode", code));
    }

    public async Task<ScriptResult> RunAsync(string script, IProgress<string>? progress, CancellationToken ct)
    {
        using var killOnCancel = ct.Register(Kill);

        await _proc.StandardInput.WriteAsync($"{script.Length}\n");
        await _proc.StandardInput.WriteAsync(script);
        await _proc.StandardInput.FlushAsync(CancellationToken.None);

        var output = new StringBuilder();
        var errors = new StringBuilder();
        while (await _proc.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            if (line.StartsWith(EndPrefix, StringComparison.Ordinal))
            {
                LastUsedTicks = Stopwatch.GetTimestamp();
                return new ScriptResult(output.ToString(), errors.ToString().Trim(),
                                        int.TryParse(line.AsSpan(EndPrefix.Length), out var c) ? c : 1);
            }
            if (line.StartsWith(ErrPrefix, StringComparison.Ordinal))
            {
                errors.AppendLine(line[ErrPrefix.Length..]);
                continue;
            }
            output.AppendLine(line);
            if (progress is not null && !string.IsNullOrWhiteSpace(line)) progress.Report(line.Trim());
        }

        LastUsedTicks = Stopwatch.GetTimestamp();
        // Stream closed: the worker was killed (cancel/timeout) or the script called "exit"
        ct.ThrowIfCancellationRequested();
        await _proc.WaitForExitAsync(CancellationToken.None);
        return new ScriptResult(output.ToString(), errors.ToString().Trim(), _proc.ExitCode);
    }

    private void Kill()
    {
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
    }

    public void Dispose()
    {
        try { _proc.StandardInput.Close(); } catch { /* ignore */ }   // ends the request loop
        if (!_proc.WaitForExit(500)) Kill();
        _proc.Dispose();
    }
}
