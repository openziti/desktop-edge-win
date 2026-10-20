using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using ZitiDesktopEdge.UITests.MockIpc;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

namespace ZitiDesktopEdge.UITests.Drivers;

// Adopted children die with testhost even when DisposeAsync never runs (xUnit timeout, taskkill, debugger detach).
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
                Step.Log($"[WARN] process {child.Id} not added to the kill-on-close job: Win32 error {Marshal.GetLastWin32Error()}");
        }
        // the child already exited
        catch (InvalidOperationException ex)
        {
            Step.Log($"[WARN] process not added to the kill-on-close job: {ex.Message}");
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

// WinAppDriver cannot resize the window and a drag on the Z grab handle misses Window_MouseDown, hence Win32.
internal static class Win32Window
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    public const uint PW_RENDERFULLCONTENT = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    public const uint SPI_GETWORKAREA = 0x0030;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    public const uint SPI_GETCOMBOBOXANIMATION = 0x1004;
    public const uint SPI_SETCOMBOBOXANIMATION = 0x1005;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetCaretBlinkTime();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCaretBlinkTime(uint uMSeconds);

    public const uint INFINITE = 0xFFFFFFFF;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
}

public sealed record TopLevelWindow(IntPtr Handle, int ProcessId, string ProcessName, string Title, Rectangle Bounds,
    bool IsForeground);

public sealed record ProcessOrigin(int ProcessId, int ParentProcessId, string ParentName, string CreationDate,
    string CommandLine);

// Text that differs between controllers: a baseline asserts it matches Expected and skips its bounds.
public sealed record VaryingText(By Elements, Regex Expected);

public sealed class AppiumSession : IAsyncDisposable
{
    // Sized for a slow first launch.
    public static readonly TimeSpan LaunchWindowTimeout = TimeSpan.FromSeconds(60);

    private const int WindowPollIntervalMs = 40;

    private const int DriverInitBackoffMs = 80;

    public WindowsDriver Driver { get; }
    public MockIpcServer Mock { get; }
    public RelayIpcServer? Relay { get; }
    public MonitorProcess? Monitor { get; }
    // Empty except from LaunchAgainstZetAsync: the mock's identities show the same text every run.
    public IReadOnlyList<VaryingText> VaryingText { get; }
    private readonly ProcessOutputLog _ui;
    private Process UiProcess => _ui.Process;

    public IntPtr WindowHandle => UiProcess.MainWindowHandle;

    public int ProcessId => UiProcess.Id;

    public bool IsWindowVisible => Win32Window.IsWindowVisible(WindowHandle);

    // The top stays at or below 0 because an off-screen area swallows clicks.
    public void MoveWindowBy(int dx, int dy)
    {
        Rectangle r = WindowBounds();
        int top = Math.Max(0, r.Top + dy);
        Win32Window.SetWindowPos(WindowHandle, IntPtr.Zero, r.Left + dx, top, r.Width, r.Height,
            Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    }

    // Includes the transparent drop shadow margin.
    public Rectangle WindowBounds()
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT r))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"GetWindowRect failed for window handle {WindowHandle}");
        return r.ToRectangle();
    }

    public static Rectangle WorkArea()
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_GETWORKAREA, 0, out Win32Window.RECT r, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_GETWORKAREA) failed");
        return r.ToRectangle();
    }

    // PrintWindow forces alpha to 255, which is why test mode gives the window a white background.
    public byte[] CaptureWindow()
    {
        Rectangle window = WindowBounds();
        MoveCursorOffWindow(window);
        using Bitmap bitmap = PrintedWindow(WindowHandle, window.Size);
        using MemoryStream png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    // Element locations shift by WindowOffsetWithPopups, so masks don't apply.
    public byte[] CaptureWindowWithPopups()
    {
        Rectangle window = WindowBounds();
        MoveCursorOffWindow(window);
        List<TopLevelWindow> popups = Popups();
        Rectangle canvas = PopupCanvas(window, popups);
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

    public Size WindowOffsetWithPopups()
    {
        Rectangle window = WindowBounds();
        Rectangle canvas = PopupCanvas(window, Popups());
        return new Size(window.X - canvas.X, window.Y - canvas.Y);
    }

    // Bottom up, so a popup drawn above another stays on top.
    private List<TopLevelWindow> Popups()
    {
        List<TopLevelWindow> popups = TopLevelWindows()
            .Where(w => w.ProcessId == UiProcess.Id && w.Handle != WindowHandle).Reverse().ToList();
        if (popups.Count == 0)
            throw new InvalidOperationException($"the app (pid {UiProcess.Id}) shows no popup window");
        return popups;
    }

    private static Rectangle PopupCanvas(Rectangle window, IEnumerable<TopLevelWindow> popups) =>
        popups.Aggregate(window, (bounds, popup) => Rectangle.Union(bounds, popup.Bounds));

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

    // Top of the z-order first.
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
                r.ToRectangle(), hWnd == foreground);
        }).ToList();
    }

    // imageName includes the extension, e.g. "ZitiDesktopEdge.exe".
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

    // A window's process can exit while the list is built.
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

    // Long enough for WPF to take the mouse leave. A hover fade back (StyledButton's runs 0.3s) takes longer.
    private const int HoverClearMs = 250;

    public void MoveCursorOffWindow() => MoveCursorOffWindow(WindowBounds());

    // A click leaves the cursor over the window, and a capture would show whatever it hovers.
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
    private readonly bool _savedComboBoxSlide;

    private AppiumSession(WindowsDriver driver, MockIpcServer mock, RelayIpcServer? relay, MonitorProcess? monitor,
        IReadOnlyList<VaryingText> varyingText, ProcessOutputLog ui, uint savedCaretBlinkTime, bool savedComboBoxSlide)
    {
        Driver = driver;
        Mock = mock;
        Relay = relay;
        Monitor = monitor;
        VaryingText = varyingText;
        _ui = ui;
        _savedCaretBlinkTime = savedCaretBlinkTime;
        _savedComboBoxSlide = savedComboBoxSlide;
    }

    // So a capture never lands mid-slide. Session-wide until logoff, so the caller restores it.
    private static bool StopComboBoxSlide()
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_GETCOMBOBOXANIMATION, 0, out int saved, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_GETCOMBOBOXANIMATION) failed");
        SetComboBoxSlide(false);
        return saved != 0;
    }

    private static void SetComboBoxSlide(bool slide)
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_SETCOMBOBOXANIMATION, 0, new IntPtr(slide ? 1 : 0), 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"SystemParametersInfo(SPI_SETCOMBOBOXANIMATION, {slide}) failed");
    }

    // So captures always draw the caret. Session-wide until logoff, so the caller restores it.
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

    // run-ui-tests.ps1 starts this WinAppDriver, so the windows driver never spawns its own.
    private const string WinAppDriverUrl = "http://127.0.0.1:4724/wd/hub";

    public static async Task<AppiumSession> LaunchAsync(string exePath, JObject landingStatus, string uiLogPath)
    {
        RequireExe(exePath);
        string prefix = NewPipePrefix();
        MockIpcServer mock = new MockIpcServer(prefix, landingStatus);
        mock.Start();

        return await AttachAsync(exePath, prefix, mock, null, null, Array.Empty<VaryingText>(), uiLogPath);
    }

    public static async Task<AppiumSession> LaunchAgainstZetAsync(string exePath, string zetDiscriminator,
        IReadOnlyList<VaryingText> controllerText, string capturePath, string uiLogPath)
    {
        RequireExe(exePath);
        string prefix = NewPipePrefix();
        MockIpcServer mock = new MockIpcServer(prefix, new JObject());
        RelayIpcServer relay = new RelayIpcServer(prefix, zetDiscriminator, capturePath);
        await relay.StartAsync();
        mock.StartMonitorPipes();

        return await AttachAsync(exePath, prefix, mock, relay, null, controllerText, uiLogPath);
    }

    public static async Task<AppiumSession> LaunchAgainstMonitorAsync(string exePath, string monitorPath,
        JObject landingStatus, string uiLogPath, string monitorLogPath)
    {
        RequireExe(exePath);
        string prefix = NewPipePrefix();
        MockIpcServer mock = new MockIpcServer(prefix, landingStatus);
        // The monitor connects to ZET's data pipes at start.
        mock.StartDataPipes();
        MonitorProcess monitor;
        try
        {
            monitor = await MonitorProcess.StartAsync(monitorPath, prefix, monitorLogPath);
        }
        catch
        {
            await mock.DisposeAsync();
            throw;
        }
        Step.Log($"started the monitor, its log is {monitorLogPath}");

        return await AttachAsync(exePath, prefix, mock, null, monitor, Array.Empty<VaryingText>(), uiLogPath);
    }

    private static void RequireExe(string exePath)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");
    }

    // Fresh per session, so a session never connects to an earlier one's pipes.
    private static string NewPipePrefix() => $"zdew-test-{Guid.NewGuid():N}-";

    private static async Task<AppiumSession> AttachAsync(string exePath, string prefix, MockIpcServer mock,
        RelayIpcServer? relay, MonitorProcess? monitor, IReadOnlyList<VaryingText> varyingText, string uiLogPath)
    {
        uint savedCaretBlinkTime = StopCaretBlink();
        bool savedComboBoxSlide = StopComboBoxSlide();
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

        // MainWindowHandle can lag WaitForInputIdle, so poll after it.
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
                // Above the longest wait that polls Win32 only: the feedback test waits up to 150s on Explorer.
                opts.AddAdditionalAppiumOption("newCommandTimeout", 180);
                opts.AddAdditionalAppiumOption("wadUrl", WinAppDriverUrl);
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
            SetComboBoxSlide(savedComboBoxSlide);
            if (monitor != null) await monitor.DisposeAsync();
            await mock.DisposeAsync();
            if (relay != null) await relay.DisposeAsync();
            if (hwnd == IntPtr.Zero)
                throw new TimeoutException($"ZDEW's main window never appeared within {LaunchWindowTimeout} of Process.Start. Log: {uiLogPath}");
            throw new InvalidOperationException($"Appium could not attach to ZDEW window within {LaunchWindowTimeout}. Last error: {lastErr?.Message}. Log: {uiLogPath}", lastErr);
        }

        Step.Log($"attached Appium to the app's window {hwnd}");
        return new AppiumSession(driver, mock, relay, monitor, varyingText, ui, savedCaretBlinkTime, savedComboBoxSlide);
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
            Step.Log($"[WARN] Appium session quit failed: {ex.Message}");
        }
        Driver.Dispose();
        KillIfRunning(UiProcess);
        SetCaretBlinkTime(_savedCaretBlinkTime);
        SetComboBoxSlide(_savedComboBoxSlide);
        // Before the mock, or the monitor logs its ZET as crashed.
        if (Monitor != null) await Monitor.DisposeAsync();
        await Mock.DisposeAsync();
        if (Relay != null) await Relay.DisposeAsync();
        // Kill returns before the process is gone. The next test's launch must not overlap a dying UI.
        if (!UiProcess.WaitForExit(10000))
            throw new TimeoutException($"UI process {UiProcess.Id} still running 10s after Kill. Log: {_ui.LogPath}");
        AppToasts.Clear(UiProcess.StartInfo.FileName);
        _ui.Dispose();
    }
}
