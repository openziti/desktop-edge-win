using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// A child process whose stdout and stderr go to one log file, killed with its tree on dispose and adopted by
/// ChildProcessKiller so it also dies with testhost.
/// </summary>
public sealed class LoggedProcess : IAsyncDisposable
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    public string LogPath { get; }
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly object _logLock = new();

    public bool HasExited => _process.HasExited;

    private LoggedProcess(Process process, StreamWriter log, string logPath)
    {
        _process = process;
        _log = log;
        LogPath = logPath;
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

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        StreamWriter log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        Process p = Process.Start(psi) ?? throw new InvalidOperationException($"{fileName} did not start");
        ChildProcessKiller.Adopt(p);

        LoggedProcess logged = new LoggedProcess(p, log, logPath);
        p.OutputDataReceived += logged.OnOutput;
        p.ErrorDataReceived += logged.OnOutput;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return logged;
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        lock (_logLock) _log.WriteLine(e.Data);
    }

    public string ReadLog()
    {
        using FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        using CancellationTokenSource timeout = new CancellationTokenSource(ExitTimeout);
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            // a survivor holds the controller ports or the ZET pipe and breaks the next run
            throw new TimeoutException($"process {_process.Id} still running {ExitTimeout.TotalSeconds}s after Kill. Log: {LogPath}", ex);
        }
        finally
        {
            lock (_logLock) _log.Dispose();
            _process.Dispose();
        }
    }
}
