using System.Diagnostics;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using ZitiDesktopEdge.UITests.MockIpc;
using Trace = ZitiDesktopEdge.UITests.Tests.Trace;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// Windows Job Object wrapper. Assigning a child process to a job with
/// KILL_ON_JOB_CLOSE means Windows will terminate that child automatically
/// when the job handle closes -- including the case where testhost.exe is
/// killed without ever running our DisposeAsync (xUnit [Fact(Timeout=...)]
/// cancellation, taskkill, debugger detach, etc).
/// </summary>
internal static class ChildProcessKiller
{
    private static readonly IntPtr _job;

    static ChildProcessKiller()
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) return;

        var limits = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        };
        var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = limits };

        int len = Marshal.SizeOf(extended);
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(extended, buf, false);
            SetInformationJobObject(_job, JobObjectExtendedLimitInformation, buf, (uint)len);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static void Adopt(Process child)
    {
        if (_job == IntPtr.Zero) return;
        try { AssignProcessToJobObject(_job, child.Handle); } catch { /* best effort */ }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpInfo, uint cbInfo);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}

/// <summary>
/// Win32 SetWindowPos / GetWindowRect wrappers. Used to move and resize the
/// WPF window from tests when WinAppDriver's Manage().Window.Size isn't
/// supported (it throws "command cannot be supported") and touch-drag via the
/// "Z" grab handle doesn't reliably fire WPF's Window_MouseDown.
/// </summary>
internal static class Win32Window
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
}

public sealed class AppiumSession : IAsyncDisposable
{
    /// <summary>
    /// Ceiling for the time spent waiting for the WPF MainWindowHandle to
    /// materialise after Process.Start. Empirically the handle appears in
    /// ~600-900ms; longer = something is wrong, fail fast instead of masking.
    /// Polling interval is <see cref="WindowPollIntervalMs"/>.
    /// </summary>
    public static readonly TimeSpan LaunchWindowTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Poll interval while waiting for MainWindowHandle != 0.</summary>
    private const int WindowPollIntervalMs = 40;

    /// <summary>Backoff between driver-init retries while window is settling.</summary>
    private const int DriverInitBackoffMs = 80;

    public WindowsDriver Driver { get; }
    public MockIpcServer Mock { get; }
    private readonly Process _uiProcess;

    /// <summary>Top-level WPF window handle for Win32 operations.</summary>
    public IntPtr WindowHandle => _uiProcess.MainWindowHandle;

    /// <summary>Move the WPF window by (dx, dy) physical pixels. Win32 SetWindowPos.</summary>
    public void MoveWindowBy(int dx, int dy) => Trace.Time($"MoveWindowBy(dx={dx},dy={dy})", () =>
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out var r)) return;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        Win32Window.SetWindowPos(WindowHandle, IntPtr.Zero, r.Left + dx, r.Top + dy, w, h,
            Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    });

    /// <summary>Resize the WPF window by (dw, dh) physical pixels (top-left stays put).</summary>
    public void ResizeWindowBy(int dw, int dh) => Trace.Time($"ResizeWindowBy(dw={dw},dh={dh})", () =>
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out var r)) return;
        int w = (r.Right - r.Left) + dw;
        int h = (r.Bottom - r.Top) + dh;
        Win32Window.SetWindowPos(WindowHandle, IntPtr.Zero, r.Left, r.Top, w, h,
            Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    });

    private AppiumSession(WindowsDriver driver, MockIpcServer mock, Process uiProcess)
    {
        Driver = driver;
        Mock = mock;
        _uiProcess = uiProcess;
    }

    public static Task<AppiumSession> LaunchAsync(
        string exePath,
        string fixturesDir,
        Uri? appiumServer = null,
        TimeSpan? waitForWindow = null,
        string fixtureFile = "landing-status.json")
    {
        var prefix = $"zdew-test-{Guid.NewGuid():N}-";
        var mock = new MockIpcServer(prefix, fixturesDir, fixtureFile);
        return LaunchCoreAsync(exePath, prefix, mock, appiumServer, waitForWindow);
    }

    /// <summary>
    /// Launch with an inline JObject status -- useful for programmatically-built
    /// fixtures (e.g. 50 mixed identities) that don't warrant a committed file.
    /// </summary>
    public static Task<AppiumSession> LaunchAsync(
        string exePath,
        JObject landingStatus,
        Uri? appiumServer = null,
        TimeSpan? waitForWindow = null)
    {
        var prefix = $"zdew-test-{Guid.NewGuid():N}-";
        var mock = new MockIpcServer(prefix, landingStatus);
        return LaunchCoreAsync(exePath, prefix, mock, appiumServer, waitForWindow);
    }

    private static async Task<AppiumSession> LaunchCoreAsync(
        string exePath,
        string prefix,
        MockIpcServer mock,
        Uri? appiumServer,
        TimeSpan? waitForWindow)
    {
        appiumServer ??= new Uri("http://127.0.0.1:4723/");
        waitForWindow ??= LaunchWindowTimeout;

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");

        Trace.Time("MockIpcServer.Start", () => mock.Start());

        var uiProc = Trace.Time("Process.Start(ZitiDesktopEdge.exe)", () =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            psi.EnvironmentVariables["ZDEW_UI_TEST"] = "1";
            psi.EnvironmentVariables["ZDEW_IPC_PIPE_PREFIX"] = prefix;
            var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ZitiDesktopEdge.exe");
            // Assign to the test-process job so the child is terminated
            // automatically if testhost dies / skips DisposeAsync.
            ChildProcessKiller.Adopt(p);
            return p;
        });

        // Wait for the WPF MainWindowHandle to materialise. Strategy:
        //   1) WaitForInputIdle blocks until the process's main thread is
        //      pumping messages -- happens AFTER MainWindow.Show(). This is OS-
        //      kernel level signal and arrives within milliseconds of the WPF
        //      window appearing.
        //   2) Fall back to a tight polling loop if WaitForInputIdle returns
        //      true but Refresh()/MainWindowHandle hasn't caught up yet (rare).
        IntPtr hwnd = await Trace.TimeAsync("wait MainWindowHandle", async () =>
        {
            // Run the blocking wait on a thread-pool thread so we don't burn
            // our caller's async context.
            await Task.Run(() => uiProc.WaitForInputIdle((int)waitForWindow.Value.TotalMilliseconds));

            // Most of the time the hwnd is already non-zero by the time
            // WaitForInputIdle returns; one Refresh() proves it.
            var deadline = DateTime.UtcNow + waitForWindow.Value;
            while (DateTime.UtcNow < deadline)
            {
                uiProc.Refresh();
                var h = uiProc.MainWindowHandle;
                if (h != IntPtr.Zero) return h;
                await Task.Delay(WindowPollIntervalMs);
            }
            return IntPtr.Zero;
        });

        WindowsDriver? driver = null;
        Exception? lastErr = null;
        if (hwnd != IntPtr.Zero)
        {
            driver = await Trace.TimeAsync("new WindowsDriver (attach)", async () =>
            {
                var deadline = DateTime.UtcNow + waitForWindow.Value;
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var opts = new AppiumOptions
                        {
                            PlatformName = "Windows",
                            AutomationName = "Windows",
                        };
                        opts.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + hwnd.ToInt64().ToString("X"));
                        opts.AddAdditionalAppiumOption("newCommandTimeout", 60);
                        return new WindowsDriver(appiumServer, opts);
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex;
                        await Task.Delay(DriverInitBackoffMs);
                    }
                }
                return null;
            });
        }

        if (driver == null)
        {
            try { uiProc.Kill(entireProcessTree: true); } catch { }
            await mock.DisposeAsync();
            throw new InvalidOperationException($"Appium could not attach to ZDEW window within {waitForWindow}. Last error: {lastErr?.Message}", lastErr);
        }

        return new AppiumSession(driver, mock, uiProc);
    }

    public async ValueTask DisposeAsync()
    {
        try { Driver.Quit(); } catch { }
        try { Driver.Dispose(); } catch { }
        try { if (!_uiProcess.HasExited) _uiProcess.Kill(entireProcessTree: true); } catch { }
        await Mock.DisposeAsync();
    }
}
