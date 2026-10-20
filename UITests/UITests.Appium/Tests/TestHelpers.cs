using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ImageMagick;
using ImageMagick.Drawing;
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

    // Once per session, since detaching collapses DetachButton. Never resize: that turns off SizeToContent.
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

    public static JObject Fixture(string fileName) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures", fileName)));

    // StyledButton's hover darkening fades back over 0.3s after the cursor leaves.
    private const int HoverFadeSettleMs = 400;

    // The blurb's frames settle about 0.8s after the relay line, and it starts hiding at 5s.
    private const int BlurbSettleMs = 1000;

    // Longer than a frame, so an animation still running changes the next capture.
    private const int StableFrameGapMs = 200;
    private const int StableFrameTimeoutMs = 2000;

    // xUnit cancels this token when a test passes its Timeout but cannot stop the thread, so test-side waits end here.
    public static void Pause(TimeSpan delay)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        token.WaitHandle.WaitOne(delay);
        token.ThrowIfCancellationRequested();
    }

    public static void Pause(int milliseconds) => Pause(TimeSpan.FromMilliseconds(milliseconds));

    public static byte[] Capture(AppiumSession s) => SettledCapture(s, s.CaptureWindow);

    public static byte[] CaptureWithPopups(AppiumSession s) => SettledCapture(s, s.CaptureWindowWithPopups);

    private static byte[] SettledCapture(AppiumSession s, Func<byte[]> capture)
    {
        // Before the settle, so the hover fade back on the element a click left the cursor over ends inside it.
        s.MoveCursorOffWindow();
        Pause(HoverFadeSettleMs);
        return StableCapture(capture);
    }

    // Settles from shownAtUtc, not now, because a WinAppDriver click can return after ZET already replied.
    public static byte[] CaptureBlurb(AppiumSession s, DateTime shownAtUtc)
    {
        s.MoveCursorOffWindow();
        TimeSpan settleLeft = shownAtUtc.AddMilliseconds(BlurbSettleMs) - DateTime.UtcNow;
        if (settleLeft > TimeSpan.Zero)
            Pause(settleLeft);
        byte[] png = StableCapture(s.CaptureWindow);
        Step.Log($"the blurb capture settled {(DateTime.UtcNow - shownAtUtc).TotalMilliseconds:F0}ms after the relay line");
        return png;
    }

    private static byte[] StableCapture(Func<byte[]> capture)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(StableFrameTimeoutMs);
        byte[] previous = capture();
        int captures = 1;
        while (true)
        {
            Pause(StableFrameGapMs);
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

    // The app names an added identity after its JWT file, and ZET shows that until an event carries the controller's.
    public const string AddedIdentityFileName = "zdew-test-add-identity";

    /// <summary>Under ZDEW_UI_TEST the app reads the JWT to add from here instead of opening a file dialog.</summary>
    private static string TestJwtPath => Path.Combine(Path.GetTempPath(), $"{AddedIdentityFileName}.jwt");

    public static void WriteTestJwt(string jwt) => File.WriteAllText(TestJwtPath, jwt);

    public static void ClickAddIdentityWithJwt(AppiumSession s) => ClickAddIdentityMenuItem(s, "With JWT");

    public static void ClickAddIdentityWithUrl(AppiumSession s) => ClickAddIdentityMenuItem(s, "With URL");

    private static void ClickAddIdentityMenuItem(AppiumSession s, string menuItem)
    {
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the MouseLeftButtonUp bubbles up to it.
        IWebElement addText = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, addText);

        IWebElement item = WaitFor(s, By.XPath($"//*[@Name='{menuItem}']"));
        ClickAt(s, item);
    }

    public static By InIdentityRow(string identityName, string automationId) =>
        By.XPath($"{IdentityRowXPath(identityName)}//*[@AutomationId='{automationId}']");

    public static string IdentityRowXPath(string identityName) =>
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]";

    // Not row-scoped: only one row shows it, and the integration tier's row name comes from a dex claim.
    public static readonly By ExtAuthRequiredIcon = ById("ExtAuthRequired");

    public static By ById(string automationId) => new ByAutomationId(automationId);

    public static string UiLogPath(string testName) =>
        Path.Combine(RepoRoot(), "UITests", "TestResults", "logs", testName, "ui.log");

    // The monitor pipes have no relay, so ui.log is the only record of the reply.
    public static DateTime? MonitorReplyAtUtc(string testName, string op)
    {
        string path = UiLogPath(testName);
        string[] lines;
        using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader reader = new StreamReader(fs))
            lines = reader.ReadToEnd().Split('\n');
        int sent = Array.FindLastIndex(lines,
            l => l.Contains("UI-MonitorClient-send-monitor:") && l.Contains($"\"Op\":\"{op}\""));
        if (sent < 0) return null;
        int reply = Array.FindIndex(lines, sent + 1, l => l.Contains("UI-MonitorClient-read-monitor:"));
        if (reply < 0) return null;
        return UiLogLineLocalTime(lines[reply], path).ToUniversalTime();
    }

    // The layout depends on whether the machine has the app installed. Both are local time, the Z included.
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

    public static string MonitorLogPath(string testName) =>
        Path.Combine(RepoRoot(), "UITests", "TestResults", "logs", testName, "monitor.log");

    public static void SaveStep(byte[] png, string testName, string stepName)
    {
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", testName),
            $"{stepName}.png", png);
        Step.Log($"saved step {testName}/{stepName}");
    }

    public static void SaveStep(AppiumSession s, string testName, string stepName) =>
        SaveStep(Capture(s), testName, stepName);

    // Await it before the screen changes: the comparison looks up VaryingText on the live window.
    public static Task VerifyStep(AppiumSession s, byte[] png, string testName, string stepName) =>
        VerifyStepAt(s, Size.Empty, png, testName, stepName);

    // Call while the popups are still open, since their offset and VaryingText are read from the live window.
    public static Task VerifyPopupStep(AppiumSession s, byte[] png, string testName, string stepName) =>
        VerifyStepAt(s, s.WindowOffsetWithPopups(), png, testName, stepName);

    private static Task VerifyStepAt(AppiumSession s, Size windowOffset, byte[] png, string testName,
        string stepName)
    {
        SaveStep(png, testName, stepName);
        string screen = stepName.Substring(stepName.IndexOf('-') + 1);
        return CompareToBaseline($"{testName}_{screen}", png,
            verified => CompareSkippingVaryingText(s, windowOffset, png, verified));
    }

    // UIA has lagged a rebuilt identity row by up to 6s.
    private static readonly TimeSpan VaryingTextTimeout = TimeSpan.FromSeconds(10);

    // UIA can leave out a rebuilt row's elements for seconds, so the lookup repeats while the comparison fails.
    private static async Task<BaselineComparison> CompareSkippingVaryingText(AppiumSession s, Size windowOffset,
        byte[] receivedPng, byte[] verifiedPng)
    {
        DateTime deadline = DateTime.UtcNow + VaryingTextTimeout;
        int lookups = 0;
        while (true)
        {
            lookups++;
            try
            {
                IReadOnlyList<ShownText> skipped = ShownVaryingText(s, windowOffset);
                BaselineComparison result = CompareChangedPixels(receivedPng, verifiedPng,
                    skipped.Select(shown => shown.Bounds).ToList());
                if (result.Matches || s.VaryingText.Count == 0 || DateTime.UtcNow >= deadline)
                {
                    Step.Log($"skipped varying text after {lookups} lookups ["
                        + string.Join(", ", skipped.Select(shown => $"'{shown.Text}' at {shown.Bounds}")) + "]");
                    return result;
                }
            }
            // The row was rebuilt during the lookup.
            catch (StaleElementReferenceException) when (DateTime.UtcNow < deadline)
            {
            }
            await Task.Delay(500, TestContext.Current.CancellationToken);
        }
    }

    // UIA lists a replaced row's elements at zero size for a while. Empty text is left for the baseline to compare.
    private static IReadOnlyList<ShownText> ShownVaryingText(AppiumSession s, Size windowOffset)
    {
        List<ShownText> shown = new List<ShownText>();
        foreach (VaryingText varying in s.VaryingText)
        {
            foreach (AppiumElement element in s.Driver.FindElements(varying.Elements))
            {
                Rectangle area = new Rectangle(Point.Add(element.Location, windowOffset), element.Size);
                string text = element.Text;
                if (area.Width > 0 && area.Height > 0 && text.Length > 0)
                {
                    Assert.Matches(varying.Expected, text);
                    shown.Add(new ShownText(text, area));
                }
            }
        }
        return shown;
    }

    private sealed record ShownText(string Text, Rectangle Bounds);

    // Review screenshots never fail a test.
    private static void WriteReviewScreenshot(string dir, string fileName, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, fileName), png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Step.Log($"[WARN] review screenshot {dir}\\{fileName} not written: {ex.Message}");
        }
    }

    // No test's await using holds the session yet, so a throw would leave its app open over every later test.
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

    // The "MAIN" text's click bubbles to the StackPanel's ShowMenu handler.
    public static void OpenMainMenu(AppiumSession s) =>
        ClickUntil(s, By.XPath("//Text[@Name='MAIN']"), By.XPath("//*[@Name='Advanced Settings']"));

    // A count, not a deadline: one slow lookup plus a failed click can take over 6s on a CI runner.
    private const int MaxClicks = 4;

    // Navigation clicks only: WPF can drop a WinAppDriver click, and a retried toggle flips twice.
    public static void ClickUntil(AppiumSession s, By target, By expected) =>
        ClickUntil(s, target, $"{expected} is displayed", () => IsDisplayed(s, expected));

    public static void ClickUntil(AppiumSession s, By target, string description, Func<bool> condition)
    {
        int clicks = 0;
        while (clicks < MaxClicks)
        {
            ClickAtUnconfirmed(s, WaitFor(s, target), target);
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
                Pause(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"Timed out waiting until {description} after {clicks} clicks on {target}. {diagnostics}");
    }

    // WinAppDriver can drop keystrokes.
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

    public static string TextById(AppiumSession s, string id) =>
        s.Driver.FindElements(ById(id)).FirstOrDefault()?.Text ?? "";

    // An added identity's row can take about 5s to render.
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(6);

    public static IWebElement WaitFor(AppiumSession s, By by) => WaitFor(s, by, ElementTimeout);

    public static IWebElement WaitFor(AppiumSession s, By by, TimeSpan timeout)
    {
        // FindElements, not FindElement: a miss that throws across WinAppDriver's HTTP boundary costs about 500ms.
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start + timeout;
        int lastMatchCount = 0;
        bool retryAfterOverrun = false;
        while (DateTime.UtcNow < deadline || retryAfterOverrun)
        {
            DateTime lookupStart = DateTime.UtcNow;
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
            catch (StaleElementReferenceException) { }
            // A UIA search failing with 0x80040201 returns empty after about 25s, so an overrun gets one more try.
            retryAfterOverrun = !retryAfterOverrun && DateTime.UtcNow - lookupStart > timeout;
            Pause(40);
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException(
            $"Timed out waiting for {by} after {timeout.TotalMilliseconds}ms ({lastMatchCount} matched on the last try but none displayed). {diagnostics}");
    }

    // The app usually answers a click within a second, and sometimes takes several.
    public static readonly TimeSpan AppResponseTimeout = TimeSpan.FromSeconds(5);

    public static void WaitForGone(AppiumSession s, By by) =>
        WaitUntil(s, $"{by} is gone", AppResponseTimeout, () => s.Driver.FindElements(by).Count == 0);

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
            catch (StaleElementReferenceException) { }
            Pause(50);
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"Timed out after {timeout.TotalSeconds}s waiting until {description}. {diagnostics}");
    }

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

    // A touch tap reaches WPF elements with no UIA Invoke pattern, and WinAppDriver's actions take only pen and touch.
    public static void ClickAt(AppiumSession s, IWebElement el)
    {
        try
        {
            el.Click();
            return;
        }
        catch (ElementNotInteractableException)
        {
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

    // WinAppDriver can fail a click with a bare "unknown error" after about 3s, so the caller's condition decides.
    private static void ClickAtUnconfirmed(AppiumSession s, IWebElement el, By target)
    {
        try
        {
            ClickAt(s, el);
        }
        // Only the base type: its subclasses, such as a stale or missing element, name the cause and still throw.
        catch (WebDriverException ex) when (ex.GetType() == typeof(WebDriverException))
        {
            Step.Log($"WinAppDriver failed the click on {target}, the condition decides whether it landed: {ex.Message}");
        }
    }

    // Collapsed arrows are out of the tree, so one union XPath finds the active one in a single round trip.
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

    public static List<string> ListedNames(AppiumSession s) =>
        IdNameThenName.Matches(s.Driver.PageSource).Select(m => m.Groups[1].Value).ToList();

    // The sort persists in user.config, so it would leak between tests into baselines.
    public static void SortByNameAscending(AppiumSession s)
    {
        IWebElement nameHeader = WaitForId(s, "SortByName");
        if (ActiveSortArrow(s).column != "Name")
            ClickSortHeader(s, nameHeader, ("Name", "▼"));
        if (ActiveSortArrow(s).arrow != "▲")
            ClickSortHeader(s, nameHeader, ("Name", "▲"));
    }

    // Never retried: a second click flips the direction again.
    public static void ClickSortHeader(AppiumSession s, IWebElement header, (string column, string arrow) expected)
    {
        ClickAt(s, header);
        WaitUntil(s, $"the list sorts by {expected.column} {expected.arrow}", AppResponseTimeout,
            () => ActiveSortArrow(s) == expected);
    }

    // Not IdName: every IdentityItem row has one too, so it shows before details opens.
    public static void OpenIdentityDetails(AppiumSession s, string identityName) =>
        ClickUntil(s, By.XPath(IdentityRowXPath(identityName)), ById("IdentityDetailsClose"));

    // The UIA tree can take 700-1700ms to drop a closed element, hence the 2s probe.
    public static void ClickUntilGone(AppiumSession s, By target)
    {
        int clicks = 0;
        while (clicks < MaxClicks)
        {
            IWebElement? shown = s.Driver.FindElements(target).FirstOrDefault(e => e.Displayed);
            if (shown == null) return;
            ClickAtUnconfirmed(s, shown, target);
            clicks++;
            DateTime probe = DateTime.UtcNow.AddMilliseconds(2000);
            while (DateTime.UtcNow < probe)
            {
                if (!IsDisplayed(s, target))
                {
                    Step.Log($"clicked {target} until it was gone ({clicks} clicks)");
                    return;
                }
                Pause(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"{target} still shown after {clicks} clicks. {diagnostics}");
    }

    public static void DismissWelcome(AppiumSession s)
    {
        By closeXPath = By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']");
        ClickAt(s, WaitFor(s, closeXPath));
        WaitForGone(s, closeXPath);
        // A hidden docked window drops every child from the tree too, so check the landing page shows.
        WaitFor(s, By.XPath("//Text[@Name='MAIN']"));
    }

    public static void CloseIdentityDetails(AppiumSession s) =>
        ClickUntilGone(s, ById("IdentityDetailsClose"));

    public static Task VerifyPng(byte[] png, [CallerMemberName] string testName = "")
    {
        WriteReviewScreenshot(Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots"), $"{testName}.png", png);
        return CompareToBaseline(testName, png,
            verified => Task.FromResult(CompareChangedPixels(png, verified, Array.Empty<Rectangle>())));
    }

    public static byte[] Masked(AppiumSession s, byte[] png, params By[] masks) =>
        PaintedOver(png, masks.SelectMany(mask => MaskElements(s, mask)).Select(el => new Rectangle(el.Location, el.Size)),
            Brushes.Gray);

    // A fixed width wider than any text, since the element sizes to its per-run text and its bounds would change.
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

    // Not XPath: an XPath lookup walks the whole tree, and a blurb shows for only 2.5s.
    public static bool BlurbShows(AppiumSession s, string text) =>
        FindByAccessibilityId(s, "Blurb")?.GetAttribute("Name") == text;

    public static void WaitForBlurb(AppiumSession s, string text, TimeSpan timeout) =>
        WaitUntil(s, $"the blurb says '{text}'", timeout, () => BlurbShows(s, text));

    // Not FindElements: WinAppDriver's AccessibilityId FindElements can add an element with an empty ID, which throws.
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

    // WPF's run-to-run anti-aliasing stays within this colour distance.
    private static readonly Percentage PixelColorTolerance = new Percentage(2);

    // A stable screen measures 0 changed pixels and a one-line text change several hundred.
    private const int MaxChangedPixels = 50;

    // Counts changed pixels, since an average metric like Fuzz passes a changed line of text.
    // Magick.NET 15 returns the Absolute metric as a fraction, not a count, so the count comes from the diff image.
    private static BaselineComparison CompareChangedPixels(byte[] received, byte[] verified, IReadOnlyList<Rectangle> skipped)
    {
        using MagickImage receivedImage = new MagickImage(received);
        using MagickImage verifiedImage = new MagickImage(verified);
        if (receivedImage.Width != verifiedImage.Width || receivedImage.Height != verifiedImage.Height)
            return new BaselineComparison(false,
                $"the capture is {receivedImage.Width}x{receivedImage.Height}, the baseline {verifiedImage.Width}x{verifiedImage.Height}");
        foreach (Rectangle area in skipped)
        {
            Blank(receivedImage, area);
            Blank(verifiedImage, area);
        }
        receivedImage.ColorFuzz = PixelColorTolerance;
        CompareSettings settings = new CompareSettings(ErrorMetric.Absolute)
        {
            HighlightColor = MagickColors.Red,
            LowlightColor = MagickColors.White,
            MasklightColor = MagickColors.White,
        };
        using IMagickImage<ushort> diff = receivedImage.Compare(verifiedImage, settings, out double _);
        using IPixelCollection<ushort> pixels = diff.GetPixels();
        int changed = pixels.Count(pixel => MagickColors.Red.Equals(pixel.ToColor()));
        if (changed > MaxChangedPixels)
            return new BaselineComparison(false,
                $"{changed} pixels differ from the baseline by more than {PixelColorTolerance}, the limit is {MaxChangedPixels}");
        return new BaselineComparison(true, "");
    }

    private static void Blank(MagickImage image, Rectangle area) =>
        image.Draw(new DrawableFillColor(MagickColors.Black),
            new DrawableRectangle(area.Left, area.Top, area.Right - 1, area.Bottom - 1));

    private sealed record BaselineComparison(bool Matches, string Mismatch);

    // run-ui-tests.ps1 and write-ui-test-report.ps1 read these file names.
    private static async Task CompareToBaseline(string name, byte[] png,
        Func<byte[], Task<BaselineComparison>> compare)
    {
        string testClass = TestContext.Current.TestClass?.TestClassSimpleName
            ?? throw new InvalidOperationException($"no test is running to name the baseline for {name}");
        string baseline = Path.Combine(RepoRoot(), "UITests", "UITests.Appium", "Tests", $"{testClass}.{name}");
        string verifiedPath = $"{baseline}.verified.png";
        string receivedPath = $"{baseline}.received.png";
        Step.Log($"comparing {name} to its baseline");
        BaselineComparison comparison;
        if (File.Exists(verifiedPath))
        {
            comparison = await compare(File.ReadAllBytes(verifiedPath));
        }
        else
        {
            comparison = new BaselineComparison(false, "there is no baseline yet");
        }
        if (comparison.Matches)
        {
            File.Delete(receivedPath);
            return;
        }
        if (Environment.GetEnvironmentVariable("ZDEW_AUTO_VERIFY") == "1")
        {
            File.WriteAllBytes(verifiedPath, png);
            File.Delete(receivedPath);
            Step.Log($"accepted the capture as the new baseline {verifiedPath}: {comparison.Mismatch}");
            return;
        }
        File.WriteAllBytes(receivedPath, png);
        throw new BaselineMismatchException(
            $"{name} does not match {verifiedPath}: {comparison.Mismatch}. The capture is {receivedPath}, " +
            "run-ui-tests.ps1 -AutoVerify accepts it.");
    }
}

public sealed class BaselineMismatchException(string message) : Exception(message);
