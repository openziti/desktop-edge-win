using System.Runtime.CompilerServices;
using ImageMagick;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

public static class TestHelpers
{
    /// <summary>Moving the window up by this clears the tray's default position.</summary>
    public const int TestWindowMoveUpPx = 150;

    /// <summary>
    /// Detach the window from the tray through the main menu, then move it up by <see cref="TestWindowMoveUpPx"/>.
    /// Never resize it: a resize turns off the window's SizeToContent. Call once per session: detaching collapses
    /// DetachButton, so a second call times out.
    /// </summary>
    public static async Task PrepareTestWindow(AppiumSession s) =>
        await Trace.TimeAsync("PrepareTestWindow", async () =>
        {
            OpenMainMenu(s);
            IWebElement detach = WaitFor(s, By.XPath("//*[@AutomationId='DetachButton']"));
            ClickAt(s, detach);
            await Trace.Settle(300);
            s.MoveWindowBy(0, -TestWindowMoveUpPx);
            await Trace.Settle(200);
        });

    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    /// <summary>A committed status fixture from MockIpc/Fixtures, e.g. "mfa-enabled.json".</summary>
    public static JObject Fixture(string fileName) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures", fileName)));

    public static byte[] Capture(AppiumSession s) => s.CaptureWindow();

    /// <summary>Under ZDEW_UI_TEST the app reads the JWT to add from here instead of opening a file dialog.</summary>
    private static string TestJwtPath => Path.Combine(Path.GetTempPath(), "zdew-test-add-identity.jwt");

    /// <summary>
    /// A JWT carrying every field the app reads: `em` picks the enrollment path ("ott" sends AddIdentity), and a
    /// DEBUG-only Console.WriteLine throws when iss, sub, jti or aud is null.
    /// </summary>
    public static string FakeJwt()
    {
        string payload = "{\"iss\":\"mock\",\"sub\":\"mock\",\"jti\":\"mock\",\"aud\":[\"mock\"],\"em\":\"ott\"}";
        string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        return $"header.{b64}.signature";
    }

    public static void WriteFakeJwt() => File.WriteAllText(TestJwtPath, FakeJwt());

    /// <summary>Click "Add Identity", then "With JWT" in its context menu.</summary>
    public static void ClickAddIdentityWithJwt(AppiumSession s)
    {
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the MouseLeftButtonUp bubbles up to it.
        IWebElement addText = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, addText);

        IWebElement withJwt = WaitFor(s, By.XPath("//*[@Name='With JWT']"));
        ClickAt(s, withJwt);
    }

    /// <summary>An element inside the IdentityItem row holding this name.</summary>
    public static By InIdentityRow(string identityName, string automationId) => By.XPath(
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]//*[@AutomationId='{automationId}']");

    /// <summary>
    /// Writes TestResults\screenshots\&lt;testName&gt;\&lt;step&gt;.png, which the gallery shows as the test's
    /// step strip.
    /// </summary>
    public static void SaveStep(byte[] png, string testName, string stepName) =>
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", testName),
            $"{stepName}.png", png);

    public static void SaveStep(AppiumSession s, string testName, string stepName) =>
        Trace.Time($"SaveStep({stepName})", () =>
        {
            byte[] png;
            try
            {
                png = Capture(s);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.Error.WriteLine($"[WARN] step {testName}/{stepName} not captured: {ex.Message}");
                return;
            }
            SaveStep(png, testName, stepName);
        });

    /// <summary>
    /// Review screenshots never fail a test: a write failure is logged to stderr, which xUnit shows even for passing
    /// tests.
    /// </summary>
    private static void WriteReviewScreenshot(string dir, string fileName, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, fileName), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[WARN] review screenshot {dir}\\{fileName} not written: {ex.Message}");
        }
    }

    /// <summary>Click the "MAIN" text, which bubbles to the StackPanel's ShowMenu handler.</summary>
    public static void OpenMainMenu(AppiumSession s) =>
        ClickUntil(s, By.XPath("//Text[@Name='MAIN']"), By.XPath("//*[@Name='Advanced Settings']"));

    /// <summary>
    /// Click target until expected is displayed, because WPF sometimes drops a WinAppDriver click. Navigation clicks
    /// only: retrying a toggle would flip it twice.
    /// </summary>
    public static void ClickUntil(AppiumSession s, By target, By expected) =>
        Trace.Time($"ClickUntil({target} shows {expected})", () =>
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            int clicks = 0;
            while (DateTime.UtcNow < deadline)
            {
                ClickAt(s, WaitFor(s, target));
                clicks++;
                // Long enough for a slow runner's animation, so a registered click is never repeated.
                DateTime probe = DateTime.UtcNow.AddMilliseconds(1500);
                while (DateTime.UtcNow < probe)
                {
                    if (IsDisplayed(s, expected)) return;
                    Thread.Sleep(40);
                }
            }
            string diagnostics = SaveTimeoutDiagnostics(s);
            throw new TimeoutException($"{expected} never showed after {clicks} clicks on {target}. {diagnostics}");
        });

    private static bool IsDisplayed(AppiumSession s, By by)
    {
        try
        {
            return s.Driver.FindElements(by).Any(e => e.Displayed);
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

    public static IWebElement ById(AppiumSession s, string id) =>
        Trace.Time($"ById({id})",
            () => s.Driver.FindElement(By.XPath($"//*[@AutomationId='{id}']")));

    /// <summary>Element text or empty string if the element isn't in the tree.</summary>
    public static string TryGetTextById(AppiumSession s, string id) =>
        Trace.Time($"TryGetTextById({id})", () =>
        {
            try { return ById(s, id).Text ?? ""; }
            catch (NoSuchElementException) { return ""; }
        });

    // An added identity's row takes up to about 5s to render on the GitHub runner.
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(6);

    public static IWebElement WaitFor(AppiumSession s, By by)
    {
        return Trace.Time($"WaitFor({by})", () =>
        {
            // FindElements returns empty on a miss. FindElement throws instead, and the throw across the WinAppDriver
            // HTTP boundary costs about 500ms per try.
            DateTime deadline = DateTime.UtcNow + ElementTimeout;
            int lastMatchCount = 0;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    System.Collections.ObjectModel.ReadOnlyCollection<OpenQA.Selenium.Appium.AppiumElement> els = s.Driver.FindElements(by);
                    lastMatchCount = els.Count;
                    if (els.Count > 0 && els[0].Displayed) return els[0];
                }
                catch (StaleElementReferenceException) { /* retry */ }
                Thread.Sleep(40);
            }
            string diagnostics = SaveTimeoutDiagnostics(s);
            throw new TimeoutException(
                $"Timed out waiting for {by} after {ElementTimeout.TotalMilliseconds}ms ({lastMatchCount} matched on the last try but none displayed). {diagnostics}");
        });
    }

    // The app answers a click within a second locally. The slack is for the GitHub runner.
    private static readonly TimeSpan AppResponseTimeout = TimeSpan.FromSeconds(5);

    public static int CommandCount(AppiumSession s, string command) =>
        s.Mock.ReceivedCommandNames.Count(c => c == command);

    /// <summary>Wait for the app to send one more command than countBefore, and return that request.</summary>
    public static JObject WaitForCommand(AppiumSession s, string command, int countBefore)
    {
        WaitUntil(s, $"the app sends {command}", AppResponseTimeout, () => CommandCount(s, command) > countBefore);
        return s.Mock.ReceivedRequests.Last(r => (string?)r["Command"] == command);
    }

    /// <summary>Wait for an element to leave the UIA tree, the mirror of WaitFor.</summary>
    public static void WaitForGone(AppiumSession s, By by) =>
        WaitUntil(s, $"{by} is gone", AppResponseTimeout, () => s.Driver.FindElements(by).Count == 0);

    /// <summary>
    /// Poll condition until it holds, or throw with diagnostics, for waits that WaitFor, WaitForGone and
    /// WaitForCommand can't express. description completes "waiting until ..." in the timeout message.
    /// </summary>
    public static void WaitUntil(AppiumSession s, string description, TimeSpan timeout, Func<bool> condition) =>
        Trace.Time($"WaitUntil({description})", () =>
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (condition()) return;
                }
                catch (StaleElementReferenceException) { /* retry */ }
                Thread.Sleep(50);
            }
            string diagnostics = SaveTimeoutDiagnostics(s);
            throw new TimeoutException($"Timed out after {timeout.TotalSeconds}s waiting until {description}. {diagnostics}");
        });

    /// <summary>
    /// Write a window capture and the UIA page source for a failed wait, so a CI flake shows what
    /// the test saw. Returns a sentence for the exception message: where they went, or why not.
    /// </summary>
    private static string SaveTimeoutDiagnostics(AppiumSession s)
    {
        string stamp = DateTime.UtcNow.ToString("HHmmss-fff");
        string dir = Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", "timeouts");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{stamp}-page-source.xml"), s.Driver.PageSource);
            File.WriteAllBytes(Path.Combine(dir, $"{stamp}-window.png"), s.CaptureWindow());
            return $"Diagnostics saved as {dir}\\{stamp}-*.";
        }
        catch (Exception ex) when (ex is WebDriverException or IOException or System.ComponentModel.Win32Exception)
        {
            return $"Diagnostics not saved: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public static IWebElement WaitForId(AppiumSession s, string id) =>
        Trace.Time($"WaitForId({id})",
            () => WaitFor(s, By.XPath($"//*[@AutomationId='{id}']")));

    /// <summary>
    /// Click, falling back to a touch tap at the element's centre when WinAppDriver calls it not interactable. Its
    /// W3C actions only take pen and touch, and touch reaches WPF elements with no UIA Invoke pattern.
    /// </summary>
    public static void ClickAt(AppiumSession s, IWebElement el) =>
        Trace.Time("ClickAt", () => ClickAtCore(s, el));

    private static void ClickAtCore(AppiumSession s, IWebElement el)
    {
        try
        {
            el.Click();
            return;
        }
        catch (OpenQA.Selenium.ElementNotInteractableException)
        {
            // falls through to the touch tap
        }

        System.Drawing.Point loc = el.Location;
        System.Drawing.Size size = el.Size;
        int cx = loc.X + (size.Width / 2);
        int cy = loc.Y + (size.Height / 2);

        OpenQA.Selenium.Interactions.PointerInputDevice touch = new OpenQA.Selenium.Interactions.PointerInputDevice(
            OpenQA.Selenium.Interactions.PointerKind.Touch, "touch-click");
        OpenQA.Selenium.Interactions.ActionSequence seq = new OpenQA.Selenium.Interactions.ActionSequence(touch, 0);
        seq.AddAction(touch.CreatePointerMove(
            OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, cx, cy, TimeSpan.Zero));
        seq.AddAction(touch.CreatePointerDown(OpenQA.Selenium.Interactions.MouseButton.Touch));
        seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(40)));
        seq.AddAction(touch.CreatePointerUp(OpenQA.Selenium.Interactions.MouseButton.Touch));
        ((OpenQA.Selenium.IActionExecutor)s.Driver).PerformActions(
            new List<OpenQA.Selenium.Interactions.ActionSequence> { seq });
    }

    /// <summary>The IdentityItem row holding this name, whatever the sort order.</summary>
    public static IWebElement IdentityRow(AppiumSession s, string identityName) =>
        Trace.Time($"IdentityRow({identityName})", () =>
            // WaitFor, because the name's Text peer can reach the tree a few frames before its parent row attaches.
            WaitFor(s, IdentityRowXPath(identityName)));

    private static By IdentityRowXPath(string identityName) =>
        By.XPath($"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]");

    public static void OpenIdentityDetails(AppiumSession s, string identityName) =>
        ClickUntil(s, IdentityRowXPath(identityName), By.XPath("//*[@AutomationId='IdName']"));

    /// <summary>Close the bottom-of-window blurb. No-op when none is shown.</summary>
    public static void DismissBlurb(AppiumSession s) =>
        ClickUntilGone(s, By.XPath("//*[@AutomationId='BlurbClose']"));

    /// <summary>
    /// Click target until it leaves the tree, for close buttons, since WPF sometimes drops a WinAppDriver click. No-op
    /// when target isn't displayed. The UIA tree can take 700-1700ms to drop a closed element, hence the probe.
    /// </summary>
    public static void ClickUntilGone(AppiumSession s, By target) =>
        Trace.Time($"ClickUntilGone({target})", () =>
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            int clicks = 0;
            while (DateTime.UtcNow < deadline)
            {
                IWebElement? shown = s.Driver.FindElements(target).FirstOrDefault(e => e.Displayed);
                if (shown == null) return;
                ClickAt(s, shown);
                clicks++;
                DateTime probe = DateTime.UtcNow.AddMilliseconds(2000);
                while (DateTime.UtcNow < probe)
                {
                    if (!IsDisplayed(s, target)) return;
                    Thread.Sleep(40);
                }
            }
            string diagnostics = SaveTimeoutDiagnostics(s);
            throw new TimeoutException($"{target} still shown after {clicks} clicks. {diagnostics}");
        });

    /// <summary>
    /// Close the welcome screen that covers the landing page whenever the service is connected
    /// with zero identities. Throws if it never appears.
    /// </summary>
    public static void DismissWelcome(AppiumSession s) =>
        Trace.Time("DismissWelcome", () =>
        {
            By closeXPath = By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']");
            ClickAt(s, WaitFor(s, closeXPath));
            DateTime deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (s.Driver.FindElements(closeXPath).Count == 0) return;
                Thread.Sleep(30);
            }
            throw new TimeoutException("DismissWelcome: welcome screen still open after clicking Close");
        });

    /// <summary>
    /// Return from identity details to the landing list, for shared-session tests between cases. No-op when details
    /// isn't open.
    /// </summary>
    public static void CloseIdentityDetails(AppiumSession s) =>
        ClickUntilGone(s, By.XPath("//*[@AutomationId='IdentityDetailsClose']"));

    public static SettingsTask VerifyPng(byte[] png, [CallerMemberName] string? testName = null)
    {
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots"), $"{testName}.png", png);
        return ComparedToBaseline(Verify(png, "png"));
    }

    /// <summary>
    /// Compare a mid-test screen to its own baseline, SmokeTests.&lt;test&gt;_&lt;screen&gt;.verified.png, so one
    /// journey can check several screens.
    /// </summary>
    public static SettingsTask VerifyScreen(byte[] png, string screen, [CallerMemberName] string? testName = null) =>
        ComparedToBaseline(Verify(png, "png").UseMethodName($"{testName}_{screen}"));

    private static SettingsTask ComparedToBaseline(SettingsTask task)
    {
        // Byte-exact comparison fails between runs on the same machine (the connected timer shows through overlays).
        task = task.ImageMagickComparer(0.05, ErrorMetric.Fuzz);
        if (Environment.GetEnvironmentVariable("ZDEW_AUTO_VERIFY") == "1")
        {
            task = task.AutoVerify();
        }
        return task;
    }
}
