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

// Text that differs between controllers: a baseline asserts it matches Expected and skips its bounds.
public sealed record VaryingText(By Elements, Regex Expected);

public sealed class AppiumSession : IAsyncDisposable
{
    // Sized for a slow first launch.
    public static readonly TimeSpan LaunchWindowTimeout = TimeSpan.FromSeconds(60);

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

    // Includes the transparent drop shadow margin.
    public Rectangle WindowBounds()
    {
        if (!Win32Window.GetWindowRect(WindowHandle, out Win32Window.RECT windowRect))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"GetWindowRect failed for window handle {WindowHandle}");
        return windowRect.ToRectangle();
    }

    public static Rectangle WorkArea()
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_GETWORKAREA, 0, out Win32Window.RECT workArea, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_GETWORKAREA) failed");
        return workArea.ToRectangle();
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
            foreach ((IntPtr handle, Rectangle bounds) in popups.Select(popup => (popup.Handle, popup.Bounds)).Prepend((WindowHandle, window)))
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
            .Where(window => window.ProcessId == UiProcess.Id && window.Handle != WindowHandle).Reverse().ToList();
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
        IntPtr deviceContext = graphics.GetHdc();
        bool printed = Win32Window.PrintWindow(handle, deviceContext, Win32Window.PW_RENDERFULLCONTENT);
        int printError = Marshal.GetLastWin32Error();
        graphics.ReleaseHdc(deviceContext);
        if (!printed)
        {
            bitmap.Dispose();
            throw new System.ComponentModel.Win32Exception(printError, $"PrintWindow failed for window handle {handle}");
        }
        return bitmap;
    }

    // Top of the z-order first.
    public static IReadOnlyList<TopLevelWindow> TopLevelWindows()
    {
        IntPtr foreground = Win32Window.GetForegroundWindow();
        List<IntPtr> handles = new List<IntPtr>();
        if (!Win32Window.EnumWindows((handle, _) => { handles.Add(handle); return true; }, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed");
        return handles.Where(Win32Window.IsWindowVisible).Select(handle =>
        {
            System.Text.StringBuilder title = new System.Text.StringBuilder(256);
            Win32Window.GetWindowText(handle, title, title.Capacity);
            Win32Window.GetWindowThreadProcessId(handle, out uint pid);
            Win32Window.GetWindowRect(handle, out Win32Window.RECT windowRect);
            return new TopLevelWindow(handle, (int)pid, ProcessName((int)pid), title.ToString(),
                windowRect.ToRectangle(), handle == foreground);
        }).ToList();
    }

    // A window's process can exit while the list is built.
    public static string ProcessName(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "(exited)";
        }
    }

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
        // Long enough for WPF to take the mouse leave. A hover fade back (StyledButton's runs 0.3s) takes longer.
        Thread.Sleep(250);
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

    private static void SetComboBoxSlide(bool slide)
    {
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_SETCOMBOBOXANIMATION, 0, new IntPtr(Convert.ToInt32(slide)), 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"SystemParametersInfo(SPI_SETCOMBOBOXANIMATION, {slide}) failed");
    }

    public static void SetCaretBlinkTime(uint blinkTime)
    {
        if (!Win32Window.SetCaretBlinkTime(blinkTime))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                $"SetCaretBlinkTime({blinkTime}) failed");
    }

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
        // So captures always draw the caret. Session-wide until logoff, so the session restores it.
        uint savedCaretBlinkTime = Win32Window.GetCaretBlinkTime();
        if (savedCaretBlinkTime == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetCaretBlinkTime failed");
        SetCaretBlinkTime(Win32Window.INFINITE);
        // So a capture never lands mid-slide. Session-wide until logoff, so the session restores it.
        if (!Win32Window.SystemParametersInfo(Win32Window.SPI_GETCOMBOBOXANIMATION, 0, out int savedSlide, 0))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "SystemParametersInfo(SPI_GETCOMBOBOXANIMATION) failed");
        bool savedComboBoxSlide = savedSlide != 0;
        SetComboBoxSlide(false);
        ProcessOutputLog? ui = null;
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };
            startInfo.EnvironmentVariables["ZDEW_UI_TEST"] = "1";
            startInfo.EnvironmentVariables["ZDEW_IPC_PIPE_PREFIX"] = prefix;
            ui = ProcessOutputLog.Start(startInfo, uiLogPath);
            Process uiProcess = ui.Process;
            Step.Log($"started the app (pid {uiProcess.Id}), its log is {uiLogPath}");

            // MainWindowHandle can lag WaitForInputIdle, so poll after it.
            IntPtr mainWindow = IntPtr.Zero;
            DateTime windowDeadline = DateTime.UtcNow + LaunchWindowTimeout;
            await Task.Run(() => uiProcess.WaitForInputIdle((int)LaunchWindowTimeout.TotalMilliseconds));
            while (mainWindow == IntPtr.Zero && DateTime.UtcNow < windowDeadline)
            {
                uiProcess.Refresh();
                mainWindow = uiProcess.MainWindowHandle;
                if (mainWindow == IntPtr.Zero) await Task.Delay(40);
            }
            if (mainWindow == IntPtr.Zero)
                throw new TimeoutException($"ZDEW's main window never appeared within {LaunchWindowTimeout} of Process.Start. Log: {uiLogPath}");

            WindowsDriver? driver = null;
            Exception? lastAttachError = null;
            DateTime driverDeadline = DateTime.UtcNow + LaunchWindowTimeout;
            while (driver == null && DateTime.UtcNow < driverDeadline)
            {
                try
                {
                    AppiumOptions options = new AppiumOptions
                    {
                        PlatformName = "Windows",
                        AutomationName = "Windows",
                    };
                    options.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + mainWindow.ToInt64().ToString("X"));
                    // Above the longest wait that polls Win32 only: the feedback test waits up to 150s on Explorer.
                    options.AddAdditionalAppiumOption("newCommandTimeout", 180);
                    // run-ui-tests.ps1 starts this WinAppDriver, so the windows driver never spawns its own.
                    options.AddAdditionalAppiumOption("wadUrl", "http://127.0.0.1:4724/wd/hub");
                    driver = new WindowsDriver(new Uri("http://127.0.0.1:4723/"), options);
                }
                catch (Exception ex)
                {
                    lastAttachError = ex;
                    await Task.Delay(80);
                }
            }
            if (driver == null)
                throw new InvalidOperationException($"Appium could not attach to ZDEW window within {LaunchWindowTimeout}. Last error: {lastAttachError?.Message}. Log: {uiLogPath}", lastAttachError);

            Step.Log($"attached Appium to the app's window {mainWindow}");
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
