using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using ZitiDesktopEdge.UITests.MockIpc;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int X, int Y);

    /// <summary>Half the caret's blink period in ms, or 0 on failure. INFINITE means it does not blink.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetCaretBlinkTime();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCaretBlinkTime(uint uMSeconds);

    public const uint INFINITE = 0xFFFFFFFF;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
}

/// <summary>A visible top-level window, in z-order, for a failed wait's diagnostics.</summary>
public sealed record TopLevelWindow(IntPtr Handle, int ProcessId, string ProcessName, string Title, Rectangle Bounds,
    bool IsForeground);

/// <summary>A running process and what started it, for a failed wait's diagnostics.</summary>
public sealed record ProcessOrigin(int ProcessId, int ParentProcessId, string ParentName, string CreationDate,
    string CommandLine);

public sealed class AppiumSession : IAsyncDisposable
{
    /// <summary>
    /// Ceiling for MainWindowHandle to appear after Process.Start. It takes about 600-900ms locally, but the first
    /// launch on a GitHub Windows runner has taken 20s (run 36895495952).
    /// </summary>
    public static readonly TimeSpan LaunchWindowTimeout = TimeSpan.FromSeconds(30);

    private const int WindowPollIntervalMs = 40;

    private const int DriverInitBackoffMs = 80;

    public WindowsDriver Driver { get; }
    public MockIpcServer Mock { get; }
    /// <summary>Set only by LaunchAgainstZetAsync, where Mock serves just the monitor pipes.</summary>
    public RelayIpcServer? Relay { get; }
    private readonly ProcessOutputLog _ui;
    private Process _uiProcess => _ui.Process;

    public IntPtr WindowHandle => _uiProcess.MainWindowHandle;

    public bool IsWindowVisible => Win32Window.IsWindowVisible(WindowHandle);

    /// <summary>
    /// Move the WPF window by (dx, dy) physical pixels. The top edge is clamped to the screen because an off-screen
    /// area swallows clicks.
    /// </summary>
    public void MoveWindowBy(int dx, int dy)
    {
        Rectangle r = WindowRect();
        int top = Math.Max(0, r.Top + dy);
        Win32Window.SetWindowPos(WindowHandle, IntPtr.Zero, r.Left + dx, top, r.Width, r.Height,
            Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    }

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
    public byte[] CaptureWindow()
    {
        Rectangle window = WindowRect();
        MoveCursorOffWindow(window);
        using Bitmap bitmap = PrintedWindow(WindowHandle, window.Size);
        using MemoryStream png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    /// <summary>
    /// <see cref="CaptureWindow"/> with the app's visible popup windows (a ContextMenu is one) drawn over it where they
    /// sit on screen, on a white canvas that grows to hold them. A popup's transparent corners capture black, because
    /// PrintWindow forces alpha to 255. Element locations no longer match the capture's origin, so masks don't apply.
    /// </summary>
    public byte[] CaptureWindowWithPopups()
    {
        Rectangle window = WindowRect();
        MoveCursorOffWindow(window);
        // Drawn bottom up, so a popup above another stays on top.
        List<TopLevelWindow> popups = TopLevelWindows()
            .Where(w => w.ProcessId == _uiProcess.Id && w.Handle != WindowHandle).Reverse().ToList();
        if (popups.Count == 0)
            throw new InvalidOperationException($"the app (pid {_uiProcess.Id}) shows no popup window to capture");
        Rectangle canvas = popups.Aggregate(window, (bounds, popup) => Rectangle.Union(bounds, popup.Bounds));
        using Bitmap bitmap = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            foreach ((IntPtr handle, Rectangle bounds) in popups.Select(p => (p.Handle, p.Bounds)).Prepend((WindowHandle, window)))
            {
                using Bitmap printed = PrintedWindow(handle, bounds.Size);
                graphics.DrawImage(printed, bounds.X - canvas.X, bounds.Y - canvas.Y);
            }
        }
        using MemoryStream png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    private static Bitmap PrintedWindow(IntPtr handle, Size size)
    {
        Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        IntPtr hdc = graphics.GetHdc();
        bool printed = Win32Window.PrintWindow(handle, hdc, Win32Window.PW_RENDERFULLCONTENT);
        int printError = Marshal.GetLastWin32Error();
        graphics.ReleaseHdc(hdc);
        if (!printed)
        {
            bitmap.Dispose();
            throw new System.ComponentModel.Win32Exception(printError, $"PrintWindow failed for window handle {handle}");
        }
        return bitmap;
    }

    /// <summary>PNG of the whole primary screen as the user sees it, to show what covers or replaced the window.</summary>
    public static byte[] CaptureDesktop()
    {
        Rectangle screen = new Rectangle(0, 0, Win32Window.GetSystemMetrics(Win32Window.SM_CXSCREEN),
            Win32Window.GetSystemMetrics(Win32Window.SM_CYSCREEN));
        using Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
        using MemoryStream png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    /// <summary>Visible top-level windows from the top of the z-order down.</summary>
    public static IReadOnlyList<TopLevelWindow> TopLevelWindows()
    {
        IntPtr foreground = Win32Window.GetForegroundWindow();
        List<IntPtr> handles = new List<IntPtr>();
        if (!Win32Window.EnumWindows((hWnd, _) => { handles.Add(hWnd); return true; }, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed");
        return handles.Where(Win32Window.IsWindowVisible).Select(hWnd =>
        {
            System.Text.StringBuilder title = new System.Text.StringBuilder(256);
            Win32Window.GetWindowText(hWnd, title, title.Capacity);
            Win32Window.GetWindowThreadProcessId(hWnd, out uint pid);
            Win32Window.GetWindowRect(hWnd, out Win32Window.RECT r);
            return new TopLevelWindow(hWnd, (int)pid, ProcessName((int)pid), title.ToString(),
                Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), hWnd == foreground);
        }).ToList();
    }

    /// <summary>Every running process with this image name, e.g. "ZitiDesktopEdge.exe", with its parent and command line.</summary>
    public static IReadOnlyList<ProcessOrigin> ProcessOrigins(string imageName)
    {
        using System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher(
            $"SELECT ProcessId, ParentProcessId, CreationDate, CommandLine FROM Win32_Process WHERE Name = '{imageName}'");
        using System.Management.ManagementObjectCollection found = searcher.Get();
        return found.Cast<System.Management.ManagementObject>().Select(p =>
        {
            int parent = (int)(uint)p["ParentProcessId"];
            return new ProcessOrigin((int)(uint)p["ProcessId"], parent, ProcessName(parent),
                (string?)p["CreationDate"] ?? "", (string?)p["CommandLine"] ?? "");
        }).ToList();
    }

    /// <summary>The process's name, or why it has none: a window's process can exit while the list is built.</summary>
    private static string ProcessName(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch (ArgumentException)
        {
            return "(exited)";
        }
    }

    private Rectangle WindowRect()
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT r))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"GetWindowRect failed for window handle {WindowHandle}");
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    // Long enough for WPF to take the mouse leave. A hover fade back (StyledButton's runs 0.3s) takes longer.
    private const int HoverClearMs = 250;

    public void MoveCursorOffWindow() => MoveCursorOffWindow(WindowRect());

    /// <summary>
    /// Move the cursor to the work area's top left when it is over the window, since a click leaves it there and the
    /// capture would show whatever it hovers.
    /// </summary>
    private static void MoveCursorOffWindow(Rectangle window)
    {
        if (!Win32Window.GetCursorPos(out Win32Window.POINT cursor))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed");
        if (!window.Contains(cursor.X, cursor.Y)) return;
        Rectangle workArea = WorkArea();
        if (window.Contains(workArea.Left, workArea.Top))
            throw new InvalidOperationException($"the window {window} covers the work area's top left, so the cursor has nowhere to go");
        if (!Win32Window.SetCursorPos(workArea.Left, workArea.Top))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"SetCursorPos({workArea.Left}, {workArea.Top}) failed");
        Thread.Sleep(HoverClearMs);
    }

    private readonly uint _savedCaretBlinkTime;

    private AppiumSession(WindowsDriver driver, MockIpcServer mock, RelayIpcServer? relay, ProcessOutputLog ui,
        uint savedCaretBlinkTime)
    {
        Driver = driver;
        Mock = mock;
        Relay = relay;
        _ui = ui;
        _savedCaretBlinkTime = savedCaretBlinkTime;
    }

    /// <summary>
    /// Stop the caret blinking, so captures always draw it, and return the blink time to restore. The setting is
    /// session-wide until logoff.
    /// </summary>
    private static uint StopCaretBlink()
    {
        uint saved = Win32Window.GetCaretBlinkTime();
        if (saved == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetCaretBlinkTime failed");
        SetCaretBlinkTime(Win32Window.INFINITE);
        return saved;
    }

    private static void SetCaretBlinkTime(uint blinkTime)
    {
        if (!Win32Window.SetCaretBlinkTime(blinkTime))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"SetCaretBlinkTime({blinkTime}) failed");
    }

    private static readonly Uri AppiumServer = new Uri("http://127.0.0.1:4723/");

    /// <summary>
    /// Start a mock ZET and monitor serving landingStatus on fresh pipe names, launch the app against them with its
    /// console output in uiLogPath, and attach Appium to its window.
    /// </summary>
    public static async Task<AppiumSession> LaunchAsync(string exePath, JObject landingStatus, string uiLogPath)
    {
        string prefix = $"zdew-test-{Guid.NewGuid():N}-";
        MockIpcServer mock = new MockIpcServer(prefix, landingStatus);

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");

        mock.Start();

        return await AttachAsync(exePath, prefix, mock, null, uiLogPath);
    }

    /// <summary>
    /// Relay the app's data pipes to a real ziti-edge-tunnel started with -P zetDiscriminator, recording the traffic
    /// to capturePath, with a mock monitor. Launch the app against them with its console output in uiLogPath and
    /// attach Appium to its window.
    /// </summary>
    public static async Task<AppiumSession> LaunchAgainstZetAsync(string exePath, string zetDiscriminator,
        string capturePath, string uiLogPath)
    {
        string prefix = $"zdew-test-{Guid.NewGuid():N}-";
        MockIpcServer mock = new MockIpcServer(prefix, new JObject());
        RelayIpcServer relay = new RelayIpcServer(prefix, zetDiscriminator, capturePath);

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");

        await relay.StartAsync();
        mock.StartMonitorPipes();

        return await AttachAsync(exePath, prefix, mock, relay, uiLogPath);
    }

    private static async Task<AppiumSession> AttachAsync(string exePath, string prefix, MockIpcServer mock,
        RelayIpcServer? relay, string uiLogPath)
    {
        uint savedCaretBlinkTime = StopCaretBlink();
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        psi.EnvironmentVariables["ZDEW_UI_TEST"] = "1";
        psi.EnvironmentVariables["ZDEW_IPC_PIPE_PREFIX"] = prefix;
        ProcessOutputLog ui = ProcessOutputLog.Start(psi, uiLogPath);
        Process uiProc = ui.Process;
        Step.Log($"started the app (pid {uiProc.Id}), its log is {uiLogPath}");

        // WaitForInputIdle returns once the UI thread pumps messages (after MainWindow.Show), but MainWindowHandle
        // can lag it, so poll after.
        IntPtr hwnd = IntPtr.Zero;
        DateTime windowDeadline = DateTime.UtcNow + LaunchWindowTimeout;
        await Task.Run(() => uiProc.WaitForInputIdle((int)LaunchWindowTimeout.TotalMilliseconds));
        while (hwnd == IntPtr.Zero && DateTime.UtcNow < windowDeadline)
        {
            uiProc.Refresh();
            hwnd = uiProc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) await Task.Delay(WindowPollIntervalMs);
        }

        WindowsDriver? driver = null;
        Exception? lastErr = null;
        DateTime driverDeadline = DateTime.UtcNow + LaunchWindowTimeout;
        while (hwnd != IntPtr.Zero && driver == null && DateTime.UtcNow < driverDeadline)
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
                driver = new WindowsDriver(AppiumServer, opts);
            }
            catch (Exception ex)
            {
                lastErr = ex;
                await Task.Delay(DriverInitBackoffMs);
            }
        }

        if (driver == null)
        {
            KillIfRunning(uiProc);
            ui.Dispose();
            SetCaretBlinkTime(savedCaretBlinkTime);
            await mock.DisposeAsync();
            if (relay != null) await relay.DisposeAsync();
            if (hwnd == IntPtr.Zero)
                throw new TimeoutException($"ZDEW's main window never appeared within {LaunchWindowTimeout} of Process.Start. Log: {uiLogPath}");
            throw new InvalidOperationException($"Appium could not attach to ZDEW window within {LaunchWindowTimeout}. Last error: {lastErr?.Message}. Log: {uiLogPath}", lastErr);
        }

        Step.Log($"attached Appium to the app's window {hwnd}");
        return new AppiumSession(driver, mock, relay, ui, savedCaretBlinkTime);
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
        SetCaretBlinkTime(_savedCaretBlinkTime);
        await Mock.DisposeAsync();
        if (Relay != null) await Relay.DisposeAsync();
        // Kill returns before the process is gone. The next test's launch must not overlap a dying UI.
        if (!_uiProcess.WaitForExit(10000))
            throw new TimeoutException($"UI process {_uiProcess.Id} still running 10s after Kill. Log: {_ui.LogPath}");
        AppToasts.Clear(_uiProcess.StartInfo.FileName);
        _ui.Dispose();
    }
}
