using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
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
    // Once per session, since detaching collapses DetachButton. Never resize: that turns off SizeToContent.
    public static void PrepareTestWindow(AppiumSession session)
    {
        OpenMainMenu(session);
        By detach = ById("DetachButton");
        ClickAt(session, WaitFor(session, detach));
        WaitForGone(session, detach);
        // Moving up 150px clears the tray's default position. The top stays at or below 0 because an off-screen area
        // swallows clicks.
        Rectangle bounds = session.WindowBounds();
        Win32Window.SetWindowPos(session.WindowHandle, IntPtr.Zero, bounds.Left, Math.Max(0, bounds.Top - 150),
            bounds.Width, bounds.Height, Win32Window.SWP_NOZORDER | Win32Window.SWP_NOACTIVATE);
    }

    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string TestResultsDir() => Path.Combine(RepoRoot(), "UITests", "TestResults");

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    /// <summary>Debug only: a Release ZitiUpdateService.exe runs solely under the service control manager.</summary>
    public static string DefaultMonitorPath() =>
        Path.Combine(RepoRoot(), "ZitiUpdateService", "bin", "Debug", "ZitiUpdateService.exe");

    public static JObject Fixture(string fileName) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures", fileName)));

    // Longer than a frame, so an animation still running changes the next capture.
    private const int StableFrameGapMs = 200;
    // A count, not a time limit, so a slow runner gets as many comparisons as a fast one.
    private const int StableFrameMaxCaptures = 10;

    // xUnit cancels this token when a test passes its Timeout but cannot stop the thread, so test-side waits end here.
    public static void Pause(TimeSpan delay)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        token.WaitHandle.WaitOne(delay);
        token.ThrowIfCancellationRequested();
    }

    public static void Pause(int milliseconds) => Pause(TimeSpan.FromMilliseconds(milliseconds));

    public static byte[] Capture(AppiumSession session) => SettledCapture(session, session.CaptureWindow);

    public static byte[] CaptureWithPopups(AppiumSession session) => SettledCapture(session, session.CaptureWindowWithPopups);

    private static byte[] SettledCapture(AppiumSession session, Func<byte[]> capture)
    {
        // Before the settle, so the hover fade back on the element a click left the cursor over ends inside it.
        session.MoveCursorOffWindow();
        // StyledButton's hover darkening fades back over 0.3s after the cursor leaves.
        Pause(400);
        return StableCapture(capture);
    }

    // Settles from shownAtUtc, not now, because a WinAppDriver click can return after ZET already replied.
    public static byte[] CaptureBlurb(AppiumSession session, DateTime shownAtUtc)
    {
        session.MoveCursorOffWindow();
        // The blurb's frames settle about 0.8s after the relay line, and it starts hiding at 5s.
        TimeSpan settleLeft = shownAtUtc.AddMilliseconds(1000) - DateTime.UtcNow;
        if (settleLeft > TimeSpan.Zero)
            Pause(settleLeft);
        byte[] png = StableCapture(session.CaptureWindow);
        Step.Log($"the blurb capture settled {(DateTime.UtcNow - shownAtUtc).TotalMilliseconds:F0}ms after the relay line");
        return png;
    }

    private static byte[] StableCapture(Func<byte[]> capture)
    {
        DateTime start = DateTime.UtcNow;
        byte[] previous = capture();
        for (int captures = 2; captures <= StableFrameMaxCaptures; captures++)
        {
            Pause(StableFrameGapMs);
            byte[] current = capture();
            if (current.AsSpan().SequenceEqual(previous))
            {
                if (captures > 2)
                    Step.Log($"the window settled after {captures} captures");
                return current;
            }
            previous = current;
        }
        throw new InvalidOperationException(
            $"the window kept changing over {StableFrameMaxCaptures} captures {StableFrameGapMs}ms apart " +
            $"({(DateTime.UtcNow - start).TotalMilliseconds:F0}ms after the settle, no two in a row matched)");
    }

    // The app names an added identity after its JWT file, and ZET shows that until an event carries the controller's.
    public const string AddedIdentityFileName = "zdew-test-add-identity";

    private static string TestJwtPath => Path.Combine(Path.GetTempPath(), $"{AddedIdentityFileName}.jwt");

    public static void WriteTestJwt(string jwt) => File.WriteAllText(TestJwtPath, jwt);

    public static void ClickAddIdentityWithJwt(AppiumSession session)
    {
        ClickAddIdentityMenuItem(session, "With JWT");
        FileOpenDialog.Choose(session, "Open", TestJwtPath);
    }

    public static void ClickAddIdentityMenuItem(AppiumSession session, string menuItem)
    {
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the MouseLeftButtonUp bubbles up to it.
        IWebElement addText = WaitFor(session, By.XPath("//Text[@Name='ADD']"));
        ClickAt(session, addText);

        IWebElement item = WaitFor(session, By.XPath($"//*[@Name='{menuItem}']"));
        ClickAt(session, item);
    }

    public static By InIdentityRow(string identityName, string automationId) =>
        By.XPath($"{IdentityRowXPath(identityName)}//*[@AutomationId='{automationId}']");

    public static string IdentityRowXPath(string identityName) =>
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]";

    public static readonly By IdentityRows = By.XPath("//Custom[@ClassName='IdentityItem']");

    // Not row-scoped: only one row shows it, and the integration tier's row name comes from a dex claim.
    public static readonly By ExtAuthRequiredIcon = ById("ExtAuthRequired");

    public static By ById(string automationId) => new ByAutomationId(automationId);

    public static By ErrorTitle(string text) => By.XPath($"//*[@AutomationId='ErrorTitle' and @Name='{text}']");

    public static readonly By CloseErrorButton = By.XPath("//Button[@Name='Close Error']");

    public static readonly By WelcomeClose =
        By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']");

    public static string UiLogPath(string testName) =>
        Path.Combine(TestResultsDir(), "logs", testName, "ui.log");

    public static void SaveStep(byte[] png, string testName, string stepName)
    {
        WriteReviewScreenshot(Path.Combine(TestResultsDir(), "screenshots", testName),
            $"{stepName}.png", png);
        Step.Log($"saved step {testName}/{stepName}");
    }

    public static void SaveStep(AppiumSession session, string testName, string stepName) =>
        SaveStep(Capture(session), testName, stepName);

    // Await it before the screen changes: the comparison looks up VaryingText on the live window.
    public static Task VerifyStep(AppiumSession session, byte[] png, string testName, string stepName) =>
        VerifyStepAt(session, Size.Empty, png, testName, stepName);

    // Call while the popups are still open, since their offset and VaryingText are read from the live window.
    public static Task VerifyPopupStep(AppiumSession session, byte[] png, string testName, string stepName) =>
        VerifyStepAt(session, session.WindowOffsetWithPopups(), png, testName, stepName);

    private static Task VerifyStepAt(AppiumSession session, Size windowOffset, byte[] png, string testName,
        string stepName)
    {
        SaveStep(png, testName, stepName);
        string screen = stepName.Substring(stepName.IndexOf('-') + 1);
        // UIA can leave out a rebuilt row's elements for seconds, so the lookup repeats while the comparison fails.
        return CompareToBaseline($"{testName}_{screen}", png, async verified =>
        {
            // UIA has lagged a rebuilt identity row by up to 6s.
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            int lookups = 0;
            while (true)
            {
                lookups++;
                try
                {
                    // UIA lists a replaced row's elements at zero size for a while. Empty text is left for the
                    // baseline to compare.
                    List<ShownText> skipped = new List<ShownText>();
                    foreach (VaryingText varying in session.VaryingText)
                    {
                        foreach (AppiumElement element in session.Driver.FindElements(varying.Elements))
                        {
                            Rectangle area = new Rectangle(Point.Add(element.Location, windowOffset), element.Size);
                            string text = element.Text;
                            if (area.Width > 0 && area.Height > 0 && text.Length > 0)
                            {
                                Assert.Matches(varying.Expected, text);
                                skipped.Add(new ShownText(text, area));
                            }
                        }
                    }
                    BaselineComparison result = CompareChangedPixels(png, verified,
                        skipped.Select(shown => shown.Bounds).ToList());
                    if (result.Matches || session.VaryingText.Count == 0 || DateTime.UtcNow >= deadline)
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
        });
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
    public static async Task<AppiumSession> PrepareOrDispose(AppiumSession session, Action<AppiumSession> prepare)
    {
        try
        {
            prepare(session);
            return session;
        }
        catch (Exception prepareError)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception disposeError)
            {
                throw new AggregateException(prepareError, disposeError);
            }
            throw;
        }
    }

    // The "MAIN" text's click bubbles to the StackPanel's ShowMenu handler.
    public static void OpenMainMenu(AppiumSession session) =>
        ClickUntil(session, By.XPath("//Text[@Name='MAIN']"), By.XPath("//*[@Name='Advanced Settings']"));

    // A count, not a deadline: one slow lookup plus a failed click can take over 6s on a CI runner.
    private const int MaxClicks = 4;

    // Navigation clicks only: WPF can drop a WinAppDriver click, and a retried toggle flips twice.
    public static void ClickUntil(AppiumSession session, By target, By expected) =>
        ClickUntil(session, target, $"{expected} is displayed", () => IsDisplayed(session, expected));

    public static void ClickUntil(AppiumSession session, By target, string description, Func<bool> condition)
    {
        int clicks = 0;
        while (clicks < MaxClicks)
        {
            ClickAtUnconfirmed(session, WaitFor(session, target), target);
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
        string diagnostics = SaveTimeoutDiagnostics(session);
        throw new TimeoutException($"Timed out waiting until {description} after {clicks} clicks on {target}. {diagnostics}");
    }

    // WinAppDriver can drop keystrokes.
    public static void TypeUntil(AppiumSession session, By box, string text)
    {
        const int maxAttempts = 3;
        string typed = "";
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            IWebElement textBox = WaitFor(session, box);
            // IWebElement.Clear() doesn't reliably fire WPF's TextChanged.
            textBox.SendKeys(Keys.Control + "a" + Keys.Control);
            textBox.SendKeys(Keys.Delete);
            textBox.SendKeys(text);
            typed = textBox.Text;
            if (typed == text)
            {
                Step.Log($"typed into {box} ({attempt} attempts)");
                return;
            }
            Step.Log($"typed '{text}' into {box} but it reads '{typed}', retyping");
        }
        string diagnostics = SaveTimeoutDiagnostics(session);
        throw new InvalidOperationException(
            $"{box} read '{typed}' instead of '{text}' after {maxAttempts} attempts. {diagnostics}");
    }

    private static bool IsDisplayed(AppiumSession session, By target)
    {
        try
        {
            return session.Driver.FindElements(target).Any(element => element.Displayed);
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

    public static string TextById(AppiumSession session, string id) =>
        session.Driver.FindElements(ById(id)).FirstOrDefault()?.Text ?? "";

    public static void WaitForText(AppiumSession session, string id, string text, TimeSpan timeout) =>
        WaitUntil(session, $"{id} shows {text}", timeout, () => TextById(session, id) == text);

    // An added identity's row can take about 5s to render.
    public static IWebElement WaitFor(AppiumSession session, By target) => WaitFor(session, target, TimeSpan.FromSeconds(6));

    public static IWebElement WaitFor(AppiumSession session, By target, TimeSpan timeout)
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
                ReadOnlyCollection<AppiumElement> matches = session.Driver.FindElements(target);
                lastMatchCount = matches.Count;
                if (matches.Count > 0 && matches[0].Displayed)
                {
                    Step.Log($"saw {target} after {(DateTime.UtcNow - start).TotalMilliseconds:F0}ms");
                    return matches[0];
                }
            }
            catch (StaleElementReferenceException) { }
            // A UIA search failing with 0x80040201 returns empty after about 25s, so an overrun gets one more try.
            retryAfterOverrun = !retryAfterOverrun && DateTime.UtcNow - lookupStart > timeout;
            Pause(40);
        }
        string diagnostics = SaveTimeoutDiagnostics(session);
        throw new TimeoutException(
            $"Timed out waiting for {target} after {timeout.TotalMilliseconds}ms ({lastMatchCount} matched on the last try but none displayed). {diagnostics}");
    }

    // The app usually answers a click within a second, and sometimes takes several.
    public static readonly TimeSpan AppResponseTimeout = TimeSpan.FromSeconds(5);

    public static void WaitForGone(AppiumSession session, By target) =>
        WaitUntil(session, $"{target} is gone", AppResponseTimeout, () => session.Driver.FindElements(target).Count == 0);

    public static void WaitUntil(AppiumSession session, string description, TimeSpan timeout, Func<bool> condition)
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
        string diagnostics = SaveTimeoutDiagnostics(session);
        throw new TimeoutException($"Timed out after {timeout.TotalSeconds}s waiting until {description}. {diagnostics}");
    }

    public static string SaveTimeoutDiagnostics(AppiumSession session)
    {
        string stamp = DateTime.UtcNow.ToString("HHmmss-fff");
        string dir = Path.Combine(TestResultsDir(), "screenshots", "timeouts");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{stamp}-page-source.xml"), session.Driver.PageSource);
            using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, " +
                "CreationDate, CommandLine FROM Win32_Process WHERE Name = 'ZitiDesktopEdge.exe'");
            using ManagementObjectCollection processes = searcher.Get();
            File.WriteAllLines(Path.Combine(dir, $"{stamp}-windows.txt"),
                AppiumSession.TopLevelWindows().Select(window =>
                {
                    string foreground;
                    if (window.IsForeground)
                        foreground = "*";
                    else
                        foreground = " ";
                    string line = $"{foreground} {window.Handle} pid={window.ProcessId} {window.ProcessName} {window.Bounds} '{window.Title}'";
                    if (window.Handle == session.WindowHandle)
                        return line + " (app under test)";
                    return line;
                })
                .Prepend($"app under test {session.WindowHandle} visible={session.IsWindowVisible}, * marks the foreground window")
                .Concat(processes.Cast<ManagementObject>().Select(process =>
                {
                    int parent = (int)(uint)process["ParentProcessId"];
                    return $"ZitiDesktopEdge pid={(int)(uint)process["ProcessId"]} started={(string?)process["CreationDate"] ?? ""} " +
                        $"parent={parent} {AppiumSession.ProcessName(parent)} '{(string?)process["CommandLine"] ?? ""}'";
                })));
            Rectangle screen = new Rectangle(0, 0, Win32Window.GetSystemMetrics(Win32Window.SM_CXSCREEN),
                Win32Window.GetSystemMetrics(Win32Window.SM_CYSCREEN));
            using (Bitmap desktop = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
            using (MemoryStream desktopPng = new MemoryStream())
            {
                using (Graphics graphics = Graphics.FromImage(desktop))
                    graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                desktop.Save(desktopPng, ImageFormat.Png);
                File.WriteAllBytes(Path.Combine(dir, $"{stamp}-desktop.png"), desktopPng.ToArray());
            }
            File.WriteAllBytes(Path.Combine(dir, $"{stamp}-window.png"), session.CaptureWindow());
            return $"Diagnostics saved as {dir}\\{stamp}-*.";
        }
        catch (Exception ex) when (ex is WebDriverException or IOException or System.ComponentModel.Win32Exception
            or System.Management.ManagementException)
        {
            return $"Diagnostics not saved: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public static IWebElement WaitForId(AppiumSession session, string id) =>
        WaitFor(session, ById(id));

    // A touch tap reaches WPF elements with no UIA Invoke pattern, and WinAppDriver's actions take only pen and touch.
    public static void ClickAt(AppiumSession session, IWebElement element)
    {
        try
        {
            element.Click();
            return;
        }
        catch (ElementNotInteractableException)
        {
        }

        int centerX = element.Location.X + (element.Size.Width / 2);
        int centerY = element.Location.Y + (element.Size.Height / 2);
        PointerInputDevice touch = new PointerInputDevice(PointerKind.Touch, "touch-click");
        ActionSequence tap = new ActionSequence(touch, 0);
        tap.AddAction(touch.CreatePointerMove(CoordinateOrigin.Viewport, centerX, centerY, TimeSpan.Zero));
        tap.AddAction(touch.CreatePointerDown(MouseButton.Touch));
        tap.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(40)));
        tap.AddAction(touch.CreatePointerUp(MouseButton.Touch));
        ((IActionExecutor)session.Driver).PerformActions(new List<ActionSequence> { tap });
    }

    // WinAppDriver can fail a click with a bare "unknown error" after about 3s, so the caller's condition decides.
    private static void ClickAtUnconfirmed(AppiumSession session, IWebElement element, By target)
    {
        try
        {
            ClickAt(session, element);
        }
        // Only the base type: its subclasses, such as a stale or missing element, name the cause and still throw.
        catch (WebDriverException ex) when (ex.GetType() == typeof(WebDriverException))
        {
            Step.Log($"WinAppDriver failed the click on {target}, the condition decides whether it landed: {ex.Message}");
        }
    }

    // Collapsed arrows are out of the tree, so one union XPath finds the active one in a single round trip.
    public static (string column, string arrow) ActiveSortArrow(AppiumSession session)
    {
        ReadOnlyCollection<AppiumElement> arrows = session.Driver.FindElements(By.XPath(
            "//*[@AutomationId='SortByStatusArrow'] | " +
            "//*[@AutomationId='SortByNameArrow'] | " +
            "//*[@AutomationId='SortByServicesArrow']"));
        if (arrows.Count == 0) return ("", "");
        AppiumElement activeArrow = arrows[0];
        string id = activeArrow.GetAttribute("AutomationId") ?? "";
        string column = id switch
        {
            "SortByStatusArrow" => "Status",
            "SortByNameArrow" => "Name",
            "SortByServicesArrow" => "Services",
            _ => throw new InvalidOperationException($"the sort arrow XPath matched an element with AutomationId '{id}'"),
        };
        return (column, activeArrow.Text ?? "");
    }

    // WinAppDriver's page source writes AutomationId before Name.
    public static List<string> ListedNames(AppiumSession session) =>
        Regex.Matches(session.Driver.PageSource, "AutomationId=\"IdName\"[^>]*?\\bName=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToList();

    // The sort persists in user.config, so it would leak between tests into baselines.
    public static void SortAscending(AppiumSession session, string column)
    {
        IWebElement header = WaitForId(session, $"SortBy{column}");
        if (ActiveSortArrow(session).column != column)
            ClickSortHeader(session, header, (column, "▼"));
        if (ActiveSortArrow(session).arrow != "▲")
            ClickSortHeader(session, header, (column, "▲"));
    }

    // Never retried: a second click flips the direction again.
    public static void ClickSortHeader(AppiumSession session, IWebElement header, (string column, string arrow) expected)
    {
        ClickAt(session, header);
        WaitUntil(session, $"the list sorts by {expected.column} {expected.arrow}", AppResponseTimeout,
            () => ActiveSortArrow(session) == expected);
    }

    // Not IdName: every IdentityItem row has one too, so it shows before details opens.
    public static void OpenIdentityDetails(AppiumSession session, string identityName) =>
        ClickUntil(session, By.XPath(IdentityRowXPath(identityName)), ById("IdentityDetailsClose"));

    // The UIA tree can take 700-1700ms to drop a closed element, hence the 2s probe.
    public static void ClickUntilGone(AppiumSession session, By target)
    {
        int clicks = 0;
        while (clicks < MaxClicks)
        {
            IWebElement? shown = session.Driver.FindElements(target).FirstOrDefault(element => element.Displayed);
            if (shown == null) return;
            ClickAtUnconfirmed(session, shown, target);
            clicks++;
            DateTime probe = DateTime.UtcNow.AddMilliseconds(2000);
            while (DateTime.UtcNow < probe)
            {
                if (!IsDisplayed(session, target))
                {
                    Step.Log($"clicked {target} until it was gone ({clicks} clicks)");
                    return;
                }
                Pause(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(session);
        throw new TimeoutException($"{target} still shown after {clicks} clicks. {diagnostics}");
    }

    public static void CloseIdentityDetails(AppiumSession session) =>
        ClickUntilGone(session, ById("IdentityDetailsClose"));

    public static Task VerifyPng(byte[] png, string testName)
    {
        WriteReviewScreenshot(Path.Combine(TestResultsDir(), "screenshots"), $"{testName}.png", png);
        return CompareToBaseline(testName, png,
            verified => Task.FromResult(CompareChangedPixels(png, verified, Array.Empty<Rectangle>())));
    }

    public static byte[] Masked(AppiumSession session, byte[] png, params By[] masks) =>
        PaintedOver(png, masks.SelectMany(mask => MaskElements(session, mask)).Select(element => new Rectangle(element.Location, element.Size)),
            Brushes.Gray);

    // A fixed width wider than any text, since the element sizes to its per-run text and its bounds would change.
    public static byte[] MaskedCentered(AppiumSession session, byte[] png, By mask, int width, Brush background) =>
        PaintedOver(png, MaskElements(session, mask).Select(element =>
            new Rectangle(element.Location.X + (element.Size.Width / 2) - (width / 2), element.Location.Y, width, element.Size.Height)),
            background);

    private static ReadOnlyCollection<AppiumElement> MaskElements(AppiumSession session, By mask)
    {
        ReadOnlyCollection<AppiumElement> masked = session.Driver.FindElements(mask);
        if (masked.Count == 0)
            throw new NoSuchElementException($"mask {mask} matched no element, so its value would reach the baseline");
        return masked;
    }

    // Not XPath: an XPath lookup walks the whole tree, and a blurb shows for only 2.5s.
    public static void WaitForBlurb(AppiumSession session, string text, TimeSpan timeout) =>
        WaitUntil(session, $"the blurb says '{text}'", timeout,
            () => FindByAccessibilityId(session, "Blurb")?.GetAttribute("Name") == text);

    // Not FindElements: WinAppDriver's AccessibilityId FindElements can add an element with an empty ID, which throws.
    public static AppiumElement? FindByAccessibilityId(AppiumSession session, string automationId)
    {
        try
        {
            return session.Driver.FindElement(MobileBy.AccessibilityId(automationId));
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
