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
    private readonly MonitorProcess? _monitor;
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
        _monitor = monitor;
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
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_SETCOMBOBOXANIMATION, 0, new IntPtr(Convert.ToInt32(slide)), 0))
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

    // Opening a common file dialog resets the session's caret blink time to the user's setting.
    public void StopCaretBlinkAgain() => SetCaretBlinkTime(Win32Window.INFINITE);

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
        ProcessOutputLog? ui = null;
        try
        {
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
            ui = ProcessOutputLog.Start(psi, uiLogPath);
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
            if (hwnd == IntPtr.Zero)
                throw new TimeoutException($"ZDEW's main window never appeared within {LaunchWindowTimeout} of Process.Start. Log: {uiLogPath}");

            WindowsDriver? driver = null;
            Exception? lastErr = null;
            DateTime driverDeadline = DateTime.UtcNow + LaunchWindowTimeout;
            while (driver == null && DateTime.UtcNow < driverDeadline)
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
                throw new InvalidOperationException($"Appium could not attach to ZDEW window within {LaunchWindowTimeout}. Last error: {lastErr?.Message}. Log: {uiLogPath}", lastErr);

            Step.Log($"attached Appium to the app's window {hwnd}");
            return new AppiumSession(driver, mock, relay, monitor, varyingText, ui, savedCaretBlinkTime, savedComboBoxSlide);
        }
        catch
        {
            if (ui != null)
            {
                KillIfRunning(ui.Process);
                ui.Dispose();
            }
            SetCaretBlinkTime(savedCaretBlinkTime);
            SetComboBoxSlide(savedComboBoxSlide);
            if (monitor != null) await monitor.DisposeAsync();
            await mock.DisposeAsync();
            if (relay != null) await relay.DisposeAsync();
            throw;
        }
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
        // Kill returns before the process is gone. The next test's launch must not overlap a dying UI.
        if (!UiProcess.WaitForExit(10000))
            throw new TimeoutException($"UI process {UiProcess.Id} still running 10s after Kill. Log: {_ui.LogPath}");
        AppToasts.Clear(UiProcess.StartInfo.FileName);
        _ui.Dispose();
        // Before the mock, or the monitor logs its ZET as crashed.
        if (_monitor != null) await _monitor.DisposeAsync();
        await Mock.DisposeAsync();
        if (Relay != null) await Relay.DisposeAsync();
    }
}
