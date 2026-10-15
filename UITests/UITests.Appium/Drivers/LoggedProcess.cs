using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// A windowless child process whose stdout and stderr go to one log file, killed with its tree on dispose and adopted
/// by ChildProcessKiller so it also dies with testhost.
/// </summary>
public sealed class LoggedProcess : IAsyncDisposable
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    private readonly ProcessOutputLog _output;

    public string LogPath => _output.LogPath;
    public bool HasExited => _output.Process.HasExited;
    public int Id => _output.Process.Id;

    private LoggedProcess(ProcessOutputLog output)
    {
        _output = output;
    }

    public static LoggedProcess Start(string fileName, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment, string logPath)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        foreach (KeyValuePair<string, string> kv in environment) psi.Environment[kv.Key] = kv.Value;
        return new LoggedProcess(ProcessOutputLog.Start(psi, logPath));
    }

    public string ReadLog() => _output.ReadLog();

    /// <summary>Wait for a process that exits on its own and return its exit code.</summary>
    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using CancellationTokenSource cts = new CancellationTokenSource(timeout);
        try
        {
            await _output.Process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException($"process {Id} still running after {timeout.TotalSeconds}s. Log: {LogPath}", ex);
        }
        return _output.Process.ExitCode;
    }

    // A restart disposes the old process before its owner is replaced, and the owner's dispose must not throw then.
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Process process = _output.Process;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        using CancellationTokenSource timeout = new CancellationTokenSource(ExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            // a survivor holds the controller ports or the ZET pipe and breaks the next run
            throw new TimeoutException($"process {process.Id} still running {ExitTimeout.TotalSeconds}s after Kill. Log: {LogPath}", ex);
        }
        finally
        {
            _output.Dispose();
        }
    }
}
