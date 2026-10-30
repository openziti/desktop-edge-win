using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

public sealed class LoggedProcess : IAsyncDisposable
{
    private readonly ProcessOutputLog _output;

    public string LogPath => _output.LogPath;
    public bool HasExited => _output.Process.HasExited;

    private LoggedProcess(ProcessOutputLog output)
    {
        _output = output;
    }

    public static LoggedProcess Start(string fileName, IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment, string logPath)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);
        foreach (KeyValuePair<string, string> variable in environment) startInfo.Environment[variable.Key] = variable.Value;
        return new LoggedProcess(ProcessOutputLog.Start(startInfo, logPath));
    }

    public string ReadLog() => _output.ReadLog();

    public async Task<int> WaitForExitAsync(TimeSpan timeout)
    {
        using CancellationTokenSource exitTimeout = new CancellationTokenSource(timeout);
        try
        {
            await _output.Process.WaitForExitAsync(exitTimeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException(
                $"process {_output.Process.Id} still running after {timeout.TotalSeconds}s. Log: {LogPath}", ex);
        }
        return _output.Process.ExitCode;
    }

    // A restart disposes the old process before its owner is replaced, and the owner's dispose must not throw then.
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (!HasExited) _output.Process.Kill(entireProcessTree: true);
            // A survivor holds the controller ports or the ZET pipe and breaks the next run.
            await WaitForExitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            _output.Dispose();
        }
    }
}
