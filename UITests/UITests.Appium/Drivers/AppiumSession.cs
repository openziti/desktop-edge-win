using System.Diagnostics;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using ZitiDesktopEdge.UITests.MockIpc;

namespace ZitiDesktopEdge.UITests.Drivers;

public sealed class AppiumSession : IAsyncDisposable
{
    public WindowsDriver Driver { get; }
    public MockIpcServer Mock { get; }
    private readonly Process _uiProcess;

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
        waitForWindow ??= TimeSpan.FromSeconds(15);

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ZitiDesktopEdge.exe not found at: {exePath}");

        mock.Start();

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        psi.EnvironmentVariables["ZDEW_UI_TEST"] = "1";
        psi.EnvironmentVariables["ZDEW_IPC_PIPE_PREFIX"] = prefix;
        var uiProc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ZitiDesktopEdge.exe");

        WindowsDriver? driver = null;
        var deadline = DateTime.UtcNow + waitForWindow.Value;
        Exception? lastErr = null;
        while (DateTime.UtcNow < deadline)
        {
            uiProc.Refresh();
            IntPtr hwnd = uiProc.MainWindowHandle;
            if (hwnd == IntPtr.Zero)
            {
                await Task.Delay(75);
                continue;
            }
            try
            {
                var opts = new AppiumOptions
                {
                    PlatformName = "Windows",
                    AutomationName = "Windows",
                };
                opts.AddAdditionalAppiumOption("appTopLevelWindow", "0x" + hwnd.ToInt64().ToString("X"));
                opts.AddAdditionalAppiumOption("newCommandTimeout", 60);
                driver = new WindowsDriver(appiumServer, opts);
                break;
            }
            catch (Exception ex)
            {
                lastErr = ex;
                await Task.Delay(150);
            }
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
