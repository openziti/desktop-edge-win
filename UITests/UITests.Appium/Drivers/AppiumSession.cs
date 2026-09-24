using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using ZitiDesktopEdge.UITests.MockIpc;
using Trace = ZitiDesktopEdge.UITests.Tests.Trace;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// Job Object with KILL_ON_JOB_CLOSE: Windows terminates every adopted child when testhost exits, even when
/// DisposeAsync never runs (xUnit timeout cancellation, taskkill, debugger detach).
/// </summary>
internal static class ChildProcessKiller
{
    private static readonly IntPtr _job;

    static ChildProcessKiller()
    {
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) return;

        JOBOBJECT_BASIC_LIMIT_INFORMATION limits = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        };
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = limits };

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
        try
        {
            if (!AssignProcessToJobObject(_job, child.Handle))
                Console.Error.WriteLine($"[WARN] process {child.Id} not added to the kill-on-close job: Win32 error {Marshal.GetLastWin32Error()}");
        }
        // the child already exited
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[WARN] process not added to the kill-on-close job: {ex.Message}");
        }
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
}

/// <summary>
/// WinAppDriver's Manage().Window.Size throws "command cannot be supported" and a touch drag on the Z grab handle
/// doesn't reliably fire Window_MouseDown, so tests move and capture the window through Win32 directly.
/// </summary>
internal static class Win32Window
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    public const uint PW_RENDERFULLCONTENT = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    public const uint SPI_GETWORKAREA = 0x0030;
}

public sealed class AppiumSession : IAsyncDisposable
{
    /// <summary>
    /// Ceiling for MainWindowHandle to appear after Process.Start. It takes about 600-900ms locally, but a cold
    /// GitHub Windows runner misses 2s.
    /// </summary>
    public static readonly TimeSpan LaunchWindowTimeout = TimeSpan.FromSeconds(10);

    private const int WindowPollIntervalMs = 40;

    private const int DriverInitBackoffMs = 80;

    public WindowsDriver Driver { get; }
    public MockIpcServer Mock { get; }
    private readonly Process _uiProcess;

    public IntPtr WindowHandle => _uiProcess.MainWindowHandle;

    /// <summary>
    /// Move the WPF window by (dx, dy) physical pixels. The top edge is clamped to the screen because an off-screen
    /// area swallows clicks.
    /// </summary>
    public void MoveWindowBy(int dx, int dy) => Trace.Time($"MoveWindowBy(dx={dx},dy={dy})", () =>
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT r)) return;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        int top = Math.Max(0, r.Top + dy);
        Win32Window.SetWindowPos(WindowHandle, IntPtr.Zero, r.Left + dx, top, w, h,
            Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    });

    /// <summary>The window's screen rectangle, its transparent drop shadow margin included.</summary>
    public Rectangle WindowBounds()
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT r))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"GetWindowRect failed for window handle {WindowHandle}");
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>The primary screen minus the taskbar.</summary>
    public static Rectangle WorkArea()
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_GETWORKAREA, 0, out Win32Window.RECT r, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_GETWORKAREA) failed");
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>
    /// PNG of the WPF window drawn by Win32 PrintWindow. Unlike a WinAppDriver element screenshot,
    /// which copies screen pixels, this is the window's own content: taskbars, other windows and
    /// screen edges never leak into it. PrintWindow returns a transparent window's pixels with alpha forced to 255,
    /// which is why test mode gives the window a white background for the drop shadow to show against.
    /// </summary>
    public byte[] CaptureWindow() => Trace.Time("CaptureWindow", () =>
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT r))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"GetWindowRect failed for window handle {WindowHandle}");
        using Bitmap bitmap = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            bool printed = Win32Window.PrintWindow(WindowHandle, hdc, Win32Window.PW_RENDERFULLCONTENT);
            int printError = Marshal.GetLastWin32Error();
            graphics.ReleaseHdc(hdc);
            if (!printed)
                throw new System.ComponentModel.Win32Exception(printError,
                    $"PrintWindow failed for window handle {WindowHandle}");
        }
        using MemoryStream png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    });

    private AppiumSession(WindowsDriver driver, MockIpcServer mock, Process uiProcess)
    {
        Driver = driver;
        Mock = mock;
        _uiProcess = uiProcess;
    }

    private static readonly Uri AppiumServer = new Uri("http://127.0.0.1:4723/");

    /// <summary>
    /// Start a mock ZET and monitor serving landingStatus on fresh pipe names, launch the app against them, and attach
    /// Appium to its window.
    /// </summary>
    public static async Task<AppiumSession> LaunchAsync(string exePath, JObject landingStatus)
    {
        string prefix = $"zdew-test-{Guid.NewGuid():N}-";
        MockIpcServer mock = new MockIpcServer(prefix, landingStatus);

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");

        Trace.Time("MockIpcServer.Start", () => mock.Start());

        Process uiProc = Trace.Time("Process.Start(ZitiDesktopEdge.exe)", () =>
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            psi.EnvironmentVariables["ZDEW_UI_TEST"] = "1";
            psi.EnvironmentVariables["ZDEW_IPC_PIPE_PREFIX"] = prefix;
            Process p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ZitiDesktopEdge.exe");
            ChildProcessKiller.Adopt(p);
            return p;
        });

        // WaitForInputIdle returns once the UI thread pumps messages (after MainWindow.Show), but MainWindowHandle
        // can lag it, so poll after.
        IntPtr hwnd = await Trace.TimeAsync("wait MainWindowHandle", async () =>
        {
            await Task.Run(() => uiProc.WaitForInputIdle((int)LaunchWindowTimeout.TotalMilliseconds));

            DateTime deadline = DateTime.UtcNow + LaunchWindowTimeout;
            while (DateTime.UtcNow < deadline)
            {
                uiProc.Refresh();
                IntPtr h = uiProc.MainWindowHandle;
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
                DateTime deadline = DateTime.UtcNow + LaunchWindowTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        AppiumOptions opts = new AppiumOptions
                        {
                            PlatformName = "Windows",
                            AutomationName = "Windows",
                        };
                        opts.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + hwnd.ToInt64().ToString("X"));
                        opts.AddAdditionalAppiumOption("newCommandTimeout", 60);
                        return new WindowsDriver(AppiumServer, opts);
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
            KillIfRunning(uiProc);
            await mock.DisposeAsync();
            throw new InvalidOperationException($"Appium could not attach to ZDEW window within {LaunchWindowTimeout}. Last error: {lastErr?.Message}", lastErr);
        }

        return new AppiumSession(driver, mock, uiProc);
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        // it exited between the check and the Kill
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Driver.Quit();
        }
        // WinAppDriver already lost the session, typically because the app exited
        catch (OpenQA.Selenium.WebDriverException ex)
        {
            Console.Error.WriteLine($"[WARN] Appium session quit failed: {ex.Message}");
        }
        Driver.Dispose();
        KillIfRunning(_uiProcess);
        await Mock.DisposeAsync();
        // Kill returns before the process is gone. The next test's launch must not overlap a dying UI.
        if (!_uiProcess.WaitForExit(10000))
            throw new TimeoutException($"UI process {_uiProcess.Id} still running 10s after Kill");
    }
}
