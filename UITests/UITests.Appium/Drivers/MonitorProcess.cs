using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// The Debug ZitiUpdateService.exe, which runs OnStart as a console process instead of a service, serving the monitor
/// pipes under a test pipe prefix. Its settings are the running user's
/// %APPDATA%\NetFoundry\ZitiUpdateService\settings.json, shared with every other monitor that user runs.
/// </summary>
public sealed class MonitorProcess : IAsyncDisposable
{
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(30);
    private const int PipePollMs = 100;
    private const int ErrorFileNotFound = 2;

    private readonly LoggedProcess _process;

    public string LogPath => _process.LogPath;

    private MonitorProcess(LoggedProcess process)
    {
        _process = process;
    }

    /// <summary>The settings file the monitor reads at start and writes on every settings change.</summary>
    public static string SettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetFoundry", "ZitiUpdateService",
        "settings.json");

    public static async Task<MonitorProcess> StartAsync(string monitorPath, string pipePrefix, string logPath)
    {
        if (!File.Exists(monitorPath)) throw new FileNotFoundException($"ZitiUpdateService.exe not found at: {monitorPath}");

        LoggedProcess process = LoggedProcess.Start(monitorPath, Array.Empty<string>(),
            new Dictionary<string, string> { ["ZDEW_IPC_PIPE_PREFIX"] = pipePrefix }, logPath);
        MonitorProcess monitor = new MonitorProcess(process);
        try
        {
            await monitor.WaitForPipeAsync(pipePrefix + @"OpenZiti\ziti-monitor\ipc");
            await monitor.WaitForPipeAsync(pipePrefix + @"OpenZiti\ziti-monitor\events");
        }
        catch
        {
            await monitor.DisposeAsync();
            throw;
        }
        return monitor;
    }

    /// <summary>
    /// Wait for a listening instance of pipeName without connecting to it, since the monitor treats every connection as
    /// a client and logs an error for one that closes without a request.
    /// </summary>
    private async Task WaitForPipeAsync(string pipeName)
    {
        DateTime deadline = DateTime.UtcNow + PipeTimeout;
        while (!WaitNamedPipe($@"\\.\pipe\{pipeName}", PipePollMs))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorFileNotFound)
                throw new Win32Exception(error, $"WaitNamedPipe failed for pipe '{pipeName}'. Log: {_process.LogPath}");
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"ZitiUpdateService exited before opening pipe '{pipeName}'. Log {_process.LogPath}:\n{_process.ReadLog()}");
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"ZitiUpdateService opened no pipe '{pipeName}' in {PipeTimeout.TotalSeconds}s. Log: {_process.LogPath}");
            await Task.Delay(PipePollMs);
        }
    }

    public string ReadLog() => _process.ReadLog();

    public ValueTask DisposeAsync() => _process.DisposeAsync();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WaitNamedPipe(string name, int timeoutMs);
}
