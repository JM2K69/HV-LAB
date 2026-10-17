using System.Diagnostics;
using System.Text;

namespace HVLab.Services;

/// <summary>
/// Error raised when a PowerShell script fails: <see cref="Exception.Message"/> is the
/// short, user-facing message; <see cref="Details"/> holds the full stderr/stdout.
/// </summary>
public sealed class PowerShellException(string message, string details, int exitCode)
    : Exception(message)
{
    public string Details  { get; } = details;
    public int    ExitCode { get; } = exitCode;
}

/// <summary>
/// Runs scripts with Windows PowerShell 5.1. The script is streamed over stdin — never written
/// to %TEMP%, where a non-elevated process could swap it before the elevated run.
/// </summary>
public static class PowerShellRunner
{
    // Fixed path to Windows PowerShell 5.1 (System32) — intentionally not user-configurable,
    // since scripts run elevated. Hyper-V cmdlets are loaded from the Windows PowerShell module path.
    private static readonly string PowerShellExe =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");

    // Reads the script (UTF-8) from stdin, runs it, and writes error records as plain
    // one-line messages on stderr (instead of CLIXML / "At line:char" noise).
    private const string Bootstrap = """
        $ProgressPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
        $reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding $false))
        $script = $reader.ReadToEnd()
        try {
            & ([scriptblock]::Create($script)) 2>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) { [Console]::Error.WriteLine($_.Exception.Message) } else { $_ }
            }
        } catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        """;

    private static readonly string EncodedBootstrap =
        Convert.ToBase64String(Encoding.Unicode.GetBytes(Bootstrap));

    /// <summary>
    /// Runs <paramref name="script"/> and returns its stdout.
    /// Each stdout line is forwarded to <paramref name="progress"/> as it arrives.
    /// On cancellation or timeout the whole process tree is killed and
    /// <see cref="OperationCanceledException"/> / <see cref="TimeoutException"/> is thrown.
    /// </summary>
    public static async Task<string> RunAsync(
        string script,
        IProgress<string>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
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
            StandardErrorEncoding  = Encoding.UTF8
        };

        using var timeoutCts = timeout is { } t ? new CancellationTokenSource(t) : null;
        using var linked     = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossible de démarrer powershell.exe");

        using var killOnCancel = linked.Token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        await proc.StandardInput.WriteAsync(script);
        proc.StandardInput.Close();

        // Read both streams concurrently to prevent deadlocks on large output.
        var stdout     = new StringBuilder();
        var stdoutTask = Task.Run(async () =>
        {
            while (await proc.StandardOutput.ReadLineAsync() is { } line)
            {
                stdout.AppendLine(line);
                if (progress is not null && !string.IsNullOrWhiteSpace(line)) progress.Report(line.Trim());
            }
        });
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync(CancellationToken.None);
        await stdoutTask;
        var stderr = (await stderrTask).Trim();

        if (timeoutCts?.IsCancellationRequested == true && !ct.IsCancellationRequested)
            throw new TimeoutException($"Le script PowerShell a dépassé le délai de {timeout!.Value.TotalSeconds:0} s.");
        ct.ThrowIfCancellationRequested();

        var output = stdout.ToString();
        if (proc.ExitCode != 0)
        {
            var details = string.IsNullOrEmpty(stderr) ? output.Trim() : stderr;
            var message = details.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .LastOrDefault()
                          ?? $"PowerShell a retourné le code {proc.ExitCode}.";
            throw new PowerShellException(message, details, proc.ExitCode);
        }
        return output;
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
