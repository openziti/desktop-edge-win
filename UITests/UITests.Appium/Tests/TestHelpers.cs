using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
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
        By detach = By.XPath("//*[@AutomationId='DetachButton']");
        ClickAt(s, WaitFor(s, detach));
        WaitForGone(s, detach);
        s.MoveWindowBy(0, -TestWindowMoveUpPx);
    }

    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    /// <summary>A committed status fixture from MockIpc/Fixtures, e.g. "needs-ext-auth.json".</summary>
    public static JObject Fixture(string fileName) =>
        JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures", fileName)));

    // The app's fades run 0.3s, and WPF keeps redrawing text after one ends: a blurb's text changes until about 1.1s
    // after it shows. Well under 2.5s, when the blurb starts hiding.
    private const int AnimationSettleMs = 1500;

    // Longer than a frame, so an animation still running changes the next capture. The runner once drew a blurb
    // mid slide 1.6s after ZET's reply (run 37035327093), so the settle alone does not prove the screen stopped.
    private const int StableFrameGapMs = 200;
    private const int StableFrameTimeoutMs = 1000;

    public static byte[] Capture(AppiumSession s)
    {
        // Before the settle, so the hover fade back on the element a click left the cursor over ends inside it.
        s.MoveCursorOffWindow();
        Thread.Sleep(AnimationSettleMs);
        return StableCapture(s.CaptureWindow);
    }

    /// <summary><see cref="Capture"/> with the app's open popups, such as a ContextMenu, drawn over the window.</summary>
    public static byte[] CaptureWithPopups(AppiumSession s)
    {
        s.MoveCursorOffWindow();
        Thread.Sleep(AnimationSettleMs);
        return StableCapture(s.CaptureWindowWithPopups);
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
                    $"the window kept changing for {StableFrameTimeoutMs}ms after the {AnimationSettleMs}ms settle " +
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
    public static By InIdentityRow(string identityName, string automationId) => By.XPath(
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]//*[@AutomationId='{automationId}']");

    /// <summary>TestResults\logs\&lt;testName&gt;\ui.log, the app's console output for that test's session.</summary>
    public static string UiLogPath(string testName) =>
        Path.Combine(RepoRoot(), "UITests", "TestResults", "logs", testName, "ui.log");

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

    /// <summary>Click the "MAIN" text, which bubbles to the StackPanel's ShowMenu handler.</summary>
    public static void OpenMainMenu(AppiumSession s) =>
        ClickUntil(s, By.XPath("//Text[@Name='MAIN']"), By.XPath("//*[@Name='Advanced Settings']"));

    /// <summary>
    /// Click target until expected is displayed, because WPF sometimes drops a WinAppDriver click. Navigation clicks
    /// only: retrying a toggle would flip it twice.
    /// </summary>
    public static void ClickUntil(AppiumSession s, By target, By expected)
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
                if (IsDisplayed(s, expected))
                {
                    Step.Log($"clicked {target} until {expected} showed ({clicks} clicks)");
                    return;
                }
                Thread.Sleep(40);
            }
        }
        string diagnostics = SaveTimeoutDiagnostics(s);
        throw new TimeoutException($"{expected} never showed after {clicks} clicks on {target}. {diagnostics}");
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
        s.Driver.FindElements(By.XPath($"//*[@AutomationId='{id}']")).FirstOrDefault()?.Text ?? "";

    // An added identity's row takes up to about 5s to render on the GitHub runner.
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(6);

    public static IWebElement WaitFor(AppiumSession s, By by)
    {
        // FindElements returns empty on a miss. FindElement throws instead, and the throw across the WinAppDriver HTTP
        // boundary costs about 500ms per try.
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start + ElementTimeout;
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
            $"Timed out waiting for {by} after {ElementTimeout.TotalMilliseconds}ms ({lastMatchCount} matched on the last try but none displayed). {diagnostics}");
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
    private static string SaveTimeoutDiagnostics(AppiumSession s)
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
        WaitFor(s, By.XPath($"//*[@AutomationId='{id}']"));

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

    private static By IdentityRowXPath(string identityName) =>
        By.XPath($"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]");

    // Not IdName: every IdentityItem row has one too, so it shows before details opens.
    public static void OpenIdentityDetails(AppiumSession s, string identityName) =>
        ClickUntil(s, IdentityRowXPath(identityName), By.XPath("//*[@AutomationId='IdentityDetailsClose']"));

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
        ClickUntilGone(s, By.XPath("//*[@AutomationId='IdentityDetailsClose']"));

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
