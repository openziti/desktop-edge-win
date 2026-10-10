using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// Starts a process adopted by ChildProcessKiller with its stdout and stderr written to one log file. Dispose only
/// after the process exited, or its last lines are lost.
/// </summary>
public sealed class ProcessOutputLog : IDisposable
{
    public string LogPath { get; }
    public Process Process { get; }
    private readonly StreamWriter _log;
    private readonly object _logLock = new();
    private bool _closed;

    private ProcessOutputLog(Process process, StreamWriter log, string logPath)
    {
        Process = process;
        _log = log;
        LogPath = logPath;
    }

    public static ProcessOutputLog Start(ProcessStartInfo psi, string logPath)
    {
        if (psi.UseShellExecute || !psi.RedirectStandardOutput || !psi.RedirectStandardError)
            throw new ArgumentException(
                $"{psi.FileName} needs UseShellExecute=false and stdout and stderr redirected to log to {logPath}",
                nameof(psi));

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        // Appends, so a process restarted mid-run keeps its earlier lines. run-ui-tests.ps1 empties TestResults per run.
        StreamWriter log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        Process p = Process.Start(psi) ?? throw new InvalidOperationException($"{psi.FileName} did not start");
        ChildProcessKiller.Adopt(p);

        ProcessOutputLog logged = new ProcessOutputLog(p, log, logPath);
        p.OutputDataReceived += logged.OnOutput;
        p.ErrorDataReceived += logged.OnOutput;
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return logged;
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        lock (_logLock)
        {
            // a grandchild that inherited the pipe can still write after the process itself exited
            if (!_closed) _log.WriteLine(e.Data);
        }
    }

    public string ReadLog()
    {
        using FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        lock (_logLock)
        {
            _closed = true;
            _log.Dispose();
        }
        Process.Dispose();
    }
}
