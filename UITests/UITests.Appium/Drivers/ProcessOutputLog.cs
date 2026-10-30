using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

// Dispose only after the process exited, or its last lines are lost.
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

    public static ProcessOutputLog Start(ProcessStartInfo startInfo, string logPath)
    {
        if (startInfo.UseShellExecute || !startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
            throw new ArgumentException(
                $"{startInfo.FileName} needs UseShellExecute=false and stdout and stderr redirected to log to {logPath}",
                nameof(startInfo));

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        // Appends, so a restarted process keeps its earlier lines. run-ui-tests.ps1 empties TestResults per run.
        StreamWriter log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
        Process started = Process.Start(startInfo) ?? throw new InvalidOperationException($"{startInfo.FileName} did not start");
        ChildProcessKiller.Adopt(started);

        ProcessOutputLog logged = new ProcessOutputLog(started, log, logPath);
        started.OutputDataReceived += logged.OnOutput;
        started.ErrorDataReceived += logged.OnOutput;
        started.BeginOutputReadLine();
        started.BeginErrorReadLine();
        return logged;
    }

    private void OnOutput(object sender, DataReceivedEventArgs output)
    {
        if (output.Data == null) return;
        lock (_logLock)
        {
            // a grandchild that inherited the pipe can still write after the process itself exited
            if (!_closed) _log.WriteLine(output.Data);
        }
    }

    public string ReadLog()
    {
        using FileStream logFile = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using StreamReader reader = new StreamReader(logFile);
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
