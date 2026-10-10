using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ImageMagick;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Interactions;
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
    public static void PrepareTestWindow(AppiumSession s)
    {
        OpenMainMenu(s);
        By detach = ById("DetachButton");
        ClickAt(s, WaitFor(s, detach));
        WaitForGone(s, detach);
        s.MoveWindowBy(0, -TestWindowMoveUpPx);
    }

    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    /// <summary>Debug only: a Release ZitiUpdateService.exe runs solely under the service control manager.</summary>
    public static string DefaultMonitorPath() =>
        Path.Combine(RepoRoot(), "ZitiUpdateService", "bin", "Debug", "ZitiUpdateService.exe");

    /// <summary>A committed status fixture from MockIpc/Fixtures, e.g. "landing-status.json".</summary>
    public static JObject Fixture(string fileName) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures", fileName)));

    // StyledButton's hover darkening fades back over 0.3s after the cursor leaves. Anything still animating after that
    // fails StableCapture's frame match and is waited out there.
    private const int HoverFadeSettleMs = 400;

    // WPF keeps redrawing a blurb's text after its 0.3s fade ends, until about 1.1s after it shows, in steps slow
    // enough that two frames 200ms apart can match mid change. Well under 2.5s, when the blurb starts hiding.
    private const int BlurbSettleMs = 1500;

    // Longer than a frame, so an animation still running changes the next capture.
    private const int StableFrameGapMs = 200;
    private const int StableFrameTimeoutMs = 2000;

    public static byte[] Capture(AppiumSession s) => SettledCapture(s, s.CaptureWindow);

    /// <summary><see cref="Capture"/> with the app's open popups, such as a ContextMenu, drawn over the window.</summary>
    public static byte[] CaptureWithPopups(AppiumSession s) => SettledCapture(s, s.CaptureWindowWithPopups);

    private static byte[] SettledCapture(AppiumSession s, Func<byte[]> capture)
    {
        // Before the settle, so the hover fade back on the element a click left the cursor over ends inside it.
        s.MoveCursorOffWindow();
        Thread.Sleep(HoverFadeSettleMs);
        return StableCapture(capture);
    }

    // MainWindow.ShowBlurbAsync starts hiding the blurb this long after it shows.
    private const int BlurbShownMs = 2500;

    /// <summary>
    /// <see cref="Capture"/> of a blurb the app raised on a relay line received at shownAtUtc. The settle runs from that
    /// line, not from now, because a WinAppDriver click can return after ZET already replied.
    /// </summary>
    public static byte[] CaptureBlurb(AppiumSession s, DateTime shownAtUtc)
    {
        s.MoveCursorOffWindow();
        TimeSpan settleLeft = shownAtUtc.AddMilliseconds(BlurbSettleMs) - DateTime.UtcNow;
        if (settleLeft > TimeSpan.Zero)
            Thread.Sleep(settleLeft);
        byte[] png = StableCapture(s.CaptureWindow);
        double capturedAfterMs = (DateTime.UtcNow - shownAtUtc).TotalMilliseconds;
        if (capturedAfterMs > BlurbShownMs)
            throw new TimeoutException(
                $"the blurb capture settled {capturedAfterMs:F0}ms after the relay line that raised it, past the " +
                $"{BlurbShownMs}ms the blurb shows before it hides");
        return png;
    }

    /// <summary>Capture until two captures <see cref="StableFrameGapMs"/> apart match, and return the second.</summary>
    private static byte[] StableCapture(Func<byte[]> capture)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(StableFrameTimeoutMs);
        byte[] previous = capture();
        int captures = 1;
        while (true)
        {
            Thread.Sleep(StableFrameGapMs);
            byte[] current = capture();
            captures++;
            if (current.AsSpan().SequenceEqual(previous))
            {
                if (captures > 2)
                    Step.Log($"the window settled after {captures} captures");
                return current;
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"the window kept changing for {StableFrameTimeoutMs}ms after the settle " +
                    $"({captures} captures {StableFrameGapMs}ms apart, no two in a row matched)");
            previous = current;
        }
    }

    /// <summary>
    /// The identity file name the app gives every added identity, since it names it after the JWT file. ZET shows it as
    /// the identity's name until an added event carries the controller name.
    /// </summary>
    public const string AddedIdentityFileName = "zdew-test-add-identity";

    /// <summary>Under ZDEW_UI_TEST the app reads the JWT to add from here instead of opening a file dialog.</summary>
    private static string TestJwtPath => Path.Combine(Path.GetTempPath(), $"{AddedIdentityFileName}.jwt");

    public static void WriteTestJwt(string jwt) => File.WriteAllText(TestJwtPath, jwt);

    public static void ClickAddIdentityWithJwt(AppiumSession s) => ClickAddIdentityMenuItem(s, "With JWT");

    public static void ClickAddIdentityWithUrl(AppiumSession s) => ClickAddIdentityMenuItem(s, "With URL");

    /// <summary>Click "Add Identity", then menuItem in its context menu.</summary>
    private static void ClickAddIdentityMenuItem(AppiumSession s, string menuItem)
    {
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the MouseLeftButtonUp bubbles up to it.
        IWebElement addText = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, addText);

        IWebElement item = WaitFor(s, By.XPath($"//*[@Name='{menuItem}']"));
        ClickAt(s, item);
    }

    /// <summary>An element inside the IdentityItem row holding this name.</summary>
    public static By InIdentityRow(string identityName, string automationId) =>
        By.XPath($"{IdentityRowXPath(identityName)}//*[@AutomationId='{automationId}']");

    /// <summary>The IdentityItem row holding this name.</summary>
    public static string IdentityRowXPath(string identityName) =>
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]";

    // Not row-scoped: one row at a time shows it, and in the integration tier its name is the controller's, which
    // comes from a dex claim.
    public static readonly By ExtAuthRequiredIcon = ById("ExtAuthRequired");

    public static By ById(string automationId) => new ByAutomationId(automationId);

    /// <summary>TestResults\logs\&lt;testName&gt;\ui.log, the app's console output for that test's session.</summary>
    public static string UiLogPath(string testName) =>
        Path.Combine(RepoRoot(), "UITests", "TestResults", "logs", testName, "ui.log");

    /// <summary>
    /// When the app received the monitor's reply to the latest command it sent the monitor with this Op. The monitor
    /// pipes have no relay, so ui.log is the only record of it, and a UIA poll can see the result over a second later.
    /// </summary>
    public static DateTime MonitorReplyAtUtc(string testName, string op)
    {
        string path = UiLogPath(testName);
        string[] lines;
        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader reader = new StreamReader(fs))
            lines = reader.ReadToEnd().Split('\n');
        int sent = Array.FindLastIndex(lines,
            l => l.Contains("UI-MonitorClient-send-monitor:") && l.Contains($"\"Op\":\"{op}\""));
        if (sent < 0) throw new InvalidOperationException($"ui.log has no {op} sent to the monitor: {path}");
        int reply = Array.FindIndex(lines, sent + 1, l => l.Contains("UI-MonitorClient-read-monitor:"));
        if (reply < 0) throw new InvalidOperationException($"ui.log has no monitor reply after its {op}: {path}");
        return UiLogLineLocalTime(lines[reply], path).ToUniversalTime();
    }

    /// <summary>
    /// A Debug build loads ZitiDesktopEdge-log.config from the install dir, so ui.log's layout depends on whether the
    /// machine has the app installed: "[2026-10-09T16:01:21.318Z]  WARN\t..." with it, NLog's default
    /// "2026-10-09 15:41:47.6423|WARN|..." without. Both are local time, the Z included.
    /// </summary>
    private static DateTime UiLogLineLocalTime(string line, string path)
    {
        Match configured = Regex.Match(line, @"^\[(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3})Z\]");
        if (configured.Success)
            return DateTime.ParseExact(configured.Groups[1].Value, "yyyy-MM-ddTHH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        Match nlogDefault = Regex.Match(line, @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4})\|");
        if (nlogDefault.Success)
            return DateTime.ParseExact(nlogDefault.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.ffff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        throw new FormatException($"ui.log line has no timestamp in a known layout: '{line}' in {path}");
    }

    /// <summary>TestResults\logs\&lt;testName&gt;\monitor.log, the monitor's console output for that test's session.</summary>
    public static string MonitorLogPath(string testName) =>
        Path.Combine(RepoRoot(), "UITests", "TestResults", "logs", testName, "monitor.log");

    /// <summary>
    /// Writes TestResults\screenshots\&lt;testName&gt;\&lt;step&gt;.png, which the gallery shows as the test's
    /// step strip.
    /// </summary>
    public static void SaveStep(byte[] png, string testName, string stepName)
    {
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", testName),
            $"{stepName}.png", png);
        Step.Log($"saved step {testName}/{stepName}");
    }

    public static void SaveStep(AppiumSession s, string testName, string stepName) =>
        SaveStep(Capture(s), testName, stepName);

    /// <summary>
    /// Save png as step stepName and compare it to its baseline, &lt;class&gt;.&lt;test&gt;_&lt;screen&gt;.verified.png,
    /// where screen is stepName without its "NN-" order prefix. One journey can check several screens this way.
    /// </summary>
    public static SettingsTask VerifyStep(byte[] png, string testName, string stepName)
    {
        SaveStep(png, testName, stepName);
        string screen = stepName.Substring(stepName.IndexOf('-') + 1);
        Step.Log($"comparing {testName}_{screen} to its baseline");
        return ComparedToBaseline(Verify(png, "png").UseMethodName($"{testName}_{screen}"));
    }

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

    /// <summary>
    /// Run prepare on a session just launched, disposing it when prepare throws: no test's await using holds it yet,
    /// and its app would otherwise stay open over every later test's window.
    /// </summary>
    public static async Task<AppiumSession> PrepareOrDispose(AppiumSession s, Action<AppiumSession> prepare)
    {
        try
        {
            prepare(s);
            return s;
        }
        catch (Exception prepareError)
        {
            try
            {
                await s.DisposeAsync();
            }
            catch (Exception disposeError)
            {
                throw new AggregateException(prepareError, disposeError);
            }
            throw;
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
        ClickUntil(s, target, $"{expected} is displayed", () => IsDisplayed(s, expected));

    /// <summary>
    /// <see cref="ClickUntil(AppiumSession, By, By)"/> for a condition a By can't express, or that a scoped search answers
    /// faster than an XPath over the whole tree. description completes "until ...".
    /// </summary>
    public static void ClickUntil(AppiumSession s, By target, string description, Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        int clicks = 0;
        while (DateTime.UtcNow < deadline)
        {
            ClickAt(s, WaitFor(s, target));
            clicks++;
            // Long enough for a slow animation, so a registered click is never repeated.
            DateTime probe = DateTime.UtcNow.AddMilliseconds(1500);
            while (DateTime.UtcNow < probe)
            {
                if (condition())
                {
                    Step.Log($"clicked {target} until {description} ({clicks} clicks)");
                    return;
                }
                Thread.Sleep(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"Timed out waiting until {description} after {clicks} clicks on {target}. {diagnostics}");
    }

    /// <summary>Replace the box's text, retyping until it reads back exactly, since WinAppDriver can drop keystrokes.</summary>
    public static void TypeUntil(AppiumSession s, By box, string text)
    {
        const int maxAttempts = 3;
        string typed = "";
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            IWebElement el = WaitFor(s, box);
            // IWebElement.Clear() doesn't reliably fire WPF's TextChanged.
            el.SendKeys(Keys.Control + "a" + Keys.Control);
            el.SendKeys(Keys.Delete);
            el.SendKeys(text);
            typed = el.Text;
            if (typed == text)
            {
                Step.Log($"typed into {box} ({attempt} attempts)");
                return;
            }
            Step.Log($"typed '{text}' into {box} but it reads '{typed}', retyping");
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new InvalidOperationException(
            $"{box} read '{typed}' instead of '{text}' after {maxAttempts} attempts. {diagnostics}");
    }

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

    /// <summary>The element's text, or an empty string when it isn't in the tree.</summary>
    public static string TextById(AppiumSession s, string id) =>
        s.Driver.FindElements(ById(id)).FirstOrDefault()?.Text ?? "";

    // An added identity's row can take about 5s to render.
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(6);

    public static IWebElement WaitFor(AppiumSession s, By by) => WaitFor(s, by, ElementTimeout);

    public static IWebElement WaitFor(AppiumSession s, By by, TimeSpan timeout)
    {
        // FindElements returns empty on a miss. FindElement throws instead, and the throw across the WinAppDriver HTTP
        // boundary costs about 500ms per try.
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start + timeout;
        int lastMatchCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                ReadOnlyCollection<AppiumElement> els = s.Driver.FindElements(by);
                lastMatchCount = els.Count;
                if (els.Count > 0 && els[0].Displayed)
                {
                    Step.Log($"saw {by} after {(DateTime.UtcNow - start).TotalMilliseconds:F0}ms");
                    return els[0];
                }
            }
            catch (StaleElementReferenceException) { /* retry */ }
            Thread.Sleep(40);
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException(
            $"Timed out waiting for {by} after {timeout.TotalMilliseconds}ms ({lastMatchCount} matched on the last try but none displayed). {diagnostics}");
    }

    // The app usually answers a click within a second, and sometimes takes several.
    public static readonly TimeSpan AppResponseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Wait for an element to leave the UIA tree, the mirror of WaitFor.</summary>
    public static void WaitForGone(AppiumSession s, By by) =>
        WaitUntil(s, $"{by} is gone", AppResponseTimeout, () => s.Driver.FindElements(by).Count == 0);

    /// <summary>
    /// Poll condition until it holds, or throw with diagnostics, for waits that WaitFor and WaitForGone can't express. description completes "waiting until ..." in the timeout message.
    /// </summary>
    public static void WaitUntil(AppiumSession s, string description, TimeSpan timeout, Func<bool> condition)
    {
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    Step.Log($"waited {(DateTime.UtcNow - start).TotalMilliseconds:F0}ms until {description}");
                    return;
                }
            }
            catch (StaleElementReferenceException) { /* retry */ }
            Thread.Sleep(50);
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"Timed out after {timeout.TotalSeconds}s waiting until {description}. {diagnostics}");
    }

    /// <summary>
    /// Write the UIA page source, the visible top-level windows, a desktop capture and a window capture for a failed
    /// wait, so a CI flake shows what the test saw and what else was on screen. Returns a sentence for the exception
    /// message: where they went, or why not.
    /// </summary>
    public static string SaveTimeoutDiagnostics(AppiumSession s)
    {
        string stamp = DateTime.UtcNow.ToString("HHmmss-fff");
        string dir = Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", "timeouts");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{stamp}-page-source.xml"), s.Driver.PageSource);
            File.WriteAllLines(Path.Combine(dir, $"{stamp}-windows.txt"),
                AppiumSession.TopLevelWindows().Select(w =>
                    $"{(w.IsForeground ? "*" : " ")} {w.Handle} pid={w.ProcessId} {w.ProcessName} {w.Bounds} '{w.Title}'" +
                    (w.Handle == s.WindowHandle ? " (app under test)" : ""))
                .Prepend($"app under test {s.WindowHandle} visible={s.IsWindowVisible}, * marks the foreground window")
                .Concat(AppiumSession.ProcessOrigins("ZitiDesktopEdge.exe").Select(p =>
                    $"ZitiDesktopEdge pid={p.ProcessId} started={p.CreationDate} parent={p.ParentProcessId} {p.ParentName} '{p.CommandLine}'")));
            File.WriteAllBytes(Path.Combine(dir, $"{stamp}-desktop.png"), AppiumSession.CaptureDesktop());
            File.WriteAllBytes(Path.Combine(dir, $"{stamp}-window.png"), s.CaptureWindow());
            return $"Diagnostics saved as {dir}\\{stamp}-*.";
        }
        catch (Exception ex) when (ex is WebDriverException or IOException or System.ComponentModel.Win32Exception
            or System.Management.ManagementException)
        {
            return $"Diagnostics not saved: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public static IWebElement WaitForId(AppiumSession s, string id) =>
        WaitFor(s, ById(id));

    /// <summary>
    /// Click, falling back to a touch tap at the element's centre when WinAppDriver calls it not interactable. Its
    /// W3C actions only take pen and touch, and touch reaches WPF elements with no UIA Invoke pattern.
    /// </summary>
    public static void ClickAt(AppiumSession s, IWebElement el)
    {
        try
        {
            el.Click();
            return;
        }
        catch (ElementNotInteractableException)
        {
            // falls through to the touch tap
        }

        int cx = el.Location.X + (el.Size.Width / 2);
        int cy = el.Location.Y + (el.Size.Height / 2);
        PointerInputDevice touch = new PointerInputDevice(PointerKind.Touch, "touch-click");
        ActionSequence seq = new ActionSequence(touch, 0);
        seq.AddAction(touch.CreatePointerMove(CoordinateOrigin.Viewport, cx, cy, TimeSpan.Zero));
        seq.AddAction(touch.CreatePointerDown(MouseButton.Touch));
        seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(40)));
        seq.AddAction(touch.CreatePointerUp(MouseButton.Touch));
        ((IActionExecutor)s.Driver).PerformActions(new List<ActionSequence> { seq });
    }

    /// <summary>
    /// The active sort column and its arrow. Only one SortByXArrow is visible at a time, and collapsed ones are not in
    /// the tree, so one union XPath finds it in a single round trip.
    /// </summary>
    public static (string column, string arrow) ActiveSortArrow(AppiumSession s)
    {
        ReadOnlyCollection<AppiumElement> arrows = s.Driver.FindElements(By.XPath(
            "//*[@AutomationId='SortByStatusArrow'] | " +
            "//*[@AutomationId='SortByNameArrow'] | " +
            "//*[@AutomationId='SortByServicesArrow']"));
        if (arrows.Count == 0) return ("", "");
        AppiumElement el = arrows[0];
        string id = el.GetAttribute("AutomationId") ?? "";
        string col = id switch
        {
            "SortByStatusArrow" => "Status",
            "SortByNameArrow" => "Name",
            "SortByServicesArrow" => "Services",
            _ => "",
        };
        return (col, el.Text ?? "");
    }

    // WinAppDriver's page source writes AutomationId before Name.
    private static readonly Regex IdNameThenName = new Regex("AutomationId=\"IdName\"[^>]*?\\bName=\"([^\"]+)\"");

    /// <summary>The landing list's identity names, top to bottom.</summary>
    public static List<string> ListedNames(AppiumSession s) =>
        IdNameThenName.Matches(s.Driver.PageSource).Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// Sort the landing list by Name ascending, since the sort persists in the user's user.config and would otherwise
    /// leak between tests into baselines. The headers only show while an identity is listed. Each click is single
    /// and checked, because a retried header click flips the direction.
    /// </summary>
    public static void SortByNameAscending(AppiumSession s)
    {
        IWebElement nameHeader = WaitForId(s, "SortByName");
        if (ActiveSortArrow(s).column != "Name")
            ClickSortHeader(s, nameHeader, ("Name", "▼"));
        if (ActiveSortArrow(s).arrow != "▲")
            ClickSortHeader(s, nameHeader, ("Name", "▲"));
    }

    /// <summary>
    /// One click on a sort header, waited on until the active arrow is expected. SetSort puts a newly chosen column in
    /// descending order and flips the active one. Never retried, because a second click flips it again.
    /// </summary>
    public static void ClickSortHeader(AppiumSession s, IWebElement header, (string column, string arrow) expected)
    {
        ClickAt(s, header);
        WaitUntil(s, $"the list sorts by {expected.column} {expected.arrow}", AppResponseTimeout,
            () => ActiveSortArrow(s) == expected);
    }

    // Not IdName: every IdentityItem row has one too, so it shows before details opens.
    public static void OpenIdentityDetails(AppiumSession s, string identityName) =>
        ClickUntil(s, By.XPath(IdentityRowXPath(identityName)), ById("IdentityDetailsClose"));

    /// <summary>
    /// Click target until it leaves the tree, for close buttons, since WPF sometimes drops a WinAppDriver click. No-op
    /// when target isn't displayed. The UIA tree can take 700-1700ms to drop a closed element, hence the probe.
    /// </summary>
    public static void ClickUntilGone(AppiumSession s, By target)
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
                if (!IsDisplayed(s, target))
                {
                    Step.Log($"clicked {target} until it was gone ({clicks} clicks)");
                    return;
                }
                Thread.Sleep(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"{target} still shown after {clicks} clicks. {diagnostics}");
    }

    /// <summary>
    /// Close the welcome screen that covers the landing page whenever the service is connected
    /// with zero identities. Throws if it never appears.
    /// </summary>
    public static void DismissWelcome(AppiumSession s)
    {
        By closeXPath = By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']");
        ClickAt(s, WaitFor(s, closeXPath));
        WaitForGone(s, closeXPath);
        // A hidden docked window drops every child from the tree too, so check the landing page shows.
        WaitFor(s, By.XPath("//Text[@Name='MAIN']"));
    }

    /// <summary>
    /// Return from identity details to the landing list, for shared-session tests between cases. No-op when details
    /// isn't open.
    /// </summary>
    public static void CloseIdentityDetails(AppiumSession s) =>
        ClickUntilGone(s, ById("IdentityDetailsClose"));

    public static SettingsTask VerifyPng(byte[] png, [CallerMemberName] string? testName = null)
    {
        Step.Log($"comparing {testName} to its baseline");
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots"), $"{testName}.png", png);
        return ComparedToBaseline(Verify(png, "png"));
    }

    /// <summary>
    /// The capture with every element the masks match painted over, so values that change per run (QR codes, secrets,
    /// recovery codes) can sit on a baseline. A mask that matches nothing throws: its value would reach the baseline.
    /// </summary>
    public static byte[] Masked(AppiumSession s, byte[] png, params By[] masks) =>
        PaintedOver(png, masks.SelectMany(mask => MaskElements(s, mask)).Select(el => new Rectangle(el.Location, el.Size)),
            Brushes.Gray);

    /// <summary>
    /// The capture with a box of a fixed width painted over each element the mask matches, centred on it, for centred
    /// elements that size to their per-run text, whose own bounds would change the baseline. width must exceed the
    /// widest text. The box takes the background colour, because a centre rounded from an odd or even width moves its
    /// edges by a pixel.
    /// </summary>
    public static byte[] MaskedCentered(AppiumSession s, byte[] png, By mask, int width, Brush background) =>
        PaintedOver(png, MaskElements(s, mask).Select(el =>
            new Rectangle(el.Location.X + (el.Size.Width / 2) - (width / 2), el.Location.Y, width, el.Size.Height)),
            background);

    private static ReadOnlyCollection<AppiumElement> MaskElements(AppiumSession s, By mask)
    {
        ReadOnlyCollection<AppiumElement> found = s.Driver.FindElements(mask);
        if (found.Count == 0)
            throw new NoSuchElementException($"mask {mask} matched no element, so its value would reach the baseline");
        return found;
    }

    /// <summary>
    /// One UIA search, since an XPath lookup walks the whole tree and a blurb shows for only 2.5s. Collapsed, the blurb
    /// is out of the UIA tree.
    /// </summary>
    public static bool BlurbShows(AppiumSession s, string text) =>
        FindByAccessibilityId(s, "Blurb")?.GetAttribute("Name") == text;

    /// <summary>
    /// Wait until the blurb says text. An XPath wait can take long enough on a prompt screen that the capture after it
    /// misses the 2.5s blurb.
    /// </summary>
    public static void WaitForBlurb(AppiumSession s, string text, TimeSpan timeout) =>
        WaitUntil(s, $"the blurb says '{text}'", timeout, () => BlurbShows(s, text));

    /// <summary>
    /// One UIA search for the element, or null when it is not in the tree. FindElement, not FindElements:
    /// WinAppDriver's AccessibilityId FindElements can add an element with an empty ID, which Selenium throws on.
    /// </summary>
    public static AppiumElement? FindByAccessibilityId(AppiumSession s, string automationId)
    {
        try
        {
            return s.Driver.FindElement(MobileBy.AccessibilityId(automationId));
        }
        catch (NoSuchElementException)
        {
            return null;
        }
    }

    // Appium reports locations relative to the session's top-level window, the same origin as the capture.
    private static byte[] PaintedOver(byte[] png, IEnumerable<Rectangle> areas, Brush brush)
    {
        using MemoryStream input = new MemoryStream(png);
        using Bitmap bitmap = new Bitmap(input);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            foreach (Rectangle area in areas)
            {
                graphics.FillRectangle(brush, area);
            }
        }
        using MemoryStream output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    // A pixel within this colour distance of the baseline is unchanged. WPF's run-to-run anti-aliasing stays below it.
    private static readonly Percentage PixelColorTolerance = new Percentage(2);

    // A stable screen measures 0 changed pixels and a one-line text change several hundred.
    private const double MaxChangedPixels = 50;

    /// <summary>
    /// Fails on more than MaxChangedPixels pixels off by more than PixelColorTolerance. An average metric like Fuzz
    /// passes a changed line of text, because a local change barely moves an average over the whole capture.
    /// </summary>
    private static Task<CompareResult> CompareChangedPixels(Stream received, Stream verified,
        IReadOnlyDictionary<string, object> context)
    {
        using MagickImage receivedImage = new MagickImage(received);
        using MagickImage verifiedImage = new MagickImage(verified);
        if (receivedImage.Width != verifiedImage.Width || receivedImage.Height != verifiedImage.Height)
            return Task.FromResult(CompareResult.NotEqual(
                $"the capture is {receivedImage.Width}x{receivedImage.Height}, the baseline {verifiedImage.Width}x{verifiedImage.Height}"));
        receivedImage.ColorFuzz = PixelColorTolerance;
        double changed = receivedImage.Compare(verifiedImage, ErrorMetric.Absolute);
        if (changed > MaxChangedPixels)
            return Task.FromResult(CompareResult.NotEqual(
                $"{changed} pixels differ from the baseline by more than {PixelColorTolerance}, the limit is {MaxChangedPixels}"));
        return Task.FromResult(CompareResult.Equal);
    }

    private static SettingsTask ComparedToBaseline(SettingsTask task)
    {
        task = task.UseStreamComparer(CompareChangedPixels);
        if (Environment.GetEnvironmentVariable("ZDEW_AUTO_VERIFY") == "1")
        {
            task = task.AutoVerify();
        }
        return task;
    }
}
