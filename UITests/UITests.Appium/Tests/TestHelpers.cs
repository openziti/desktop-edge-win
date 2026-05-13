using System.Runtime.CompilerServices;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

public static class TestHelpers
{
    /// <summary>
    /// How far we shift the test window up + how much we grow its height in
    /// <see cref="PrepareTestWindow"/>. One knob for both directions: the move
    /// gets the title bar above the tray-icon-default position and the height
    /// growth ensures menus / scroll lists don't get clipped. Bump if 150 still
    /// crops content; the SetWindowPos calls clamp to the display.
    /// </summary>
    public const int TestWindowAdjustPx = 150;

    /// <summary>
    /// Standard window prep run at the start of any test that wants a normal
    /// taskbar window (detached from the system tray) and a bit of headroom:
    ///   1. OpenMainMenu -> click "Detach App"   (window enters normal mode)
    ///   2. MoveWindowBy(0, -TestWindowAdjustPx) (shift up via Win32 SetWindowPos)
    ///   3. ResizeWindowBy(0, +TestWindowAdjustPx) (grow height; top-left stays)
    /// Safe to call once per session. Subsequent calls open the menu only to
    /// no-op on DetachButton (already collapsed in detached state).
    /// </summary>
    public static async Task PrepareTestWindow(AppiumSession s) =>
        await Trace.TimeAsync("PrepareTestWindow", async () =>
        {
            OpenMainMenu(s);
            var detach = WaitFor(s, By.XPath("//*[@AutomationId='DetachButton']"));
            ClickAt(s, detach);
            await Trace.Settle(300);
            s.MoveWindowBy(0, -TestWindowAdjustPx);
            s.ResizeWindowBy(0, TestWindowAdjustPx);
            await Trace.Settle(200);
        });

    /// <summary>
    /// Cached PageSource fetch wrapped with Trace timing. Use this in tests
    /// instead of `s.Driver.PageSource` so the cost shows up on the trace.
    /// </summary>
    public static string PageSource(AppiumSession s) =>
        Trace.Time("Driver.PageSource", () => s.Driver.PageSource);

    /// <summary>Find one element, traced.</summary>
    public static IWebElement FindOne(AppiumSession s, By by) =>
        Trace.Time($"FindElement({by})", () => s.Driver.FindElement(by));

    /// <summary>Find many elements, traced. Use when you want to test presence.</summary>
    public static IReadOnlyCollection<IWebElement> FindMany(AppiumSession s, By by) =>
        Trace.Time($"FindElements({by})", () => s.Driver.FindElements(by));

    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    public static string FixturesDir() =>
        Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures");

    public static byte[] ElementScreenshot(IWebElement el) =>
        Trace.Time("ElementScreenshot", () => ((ITakesScreenshot)el).GetScreenshot().AsByteArray);

    /// <summary>
    /// Capture the entire ZDEW window as a PNG byte array.
    /// </summary>
    public static byte[] Capture(AppiumSession s) =>
        Trace.Time("Capture", () => ElementScreenshot(WindowElement(s)));

    /// <summary>
    /// Save a per-step screenshot under TestResults\screenshots\&lt;testName&gt;\&lt;step&gt;.png.
    /// The gallery picks these up and renders a multi-step strip for the test card.
    /// </summary>
    public static void SaveStep(byte[] png, string testName, string stepName)
    {
        try
        {
            var dir = Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots", testName);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{stepName}.png"), png);
        }
        catch { /* best effort */ }
    }

    public static void SaveStep(AppiumSession s, string testName, string stepName) =>
        Trace.Time($"SaveStep({stepName})", () =>
        {
            // Capture occasionally throws WebDriverException when an overlay opens
            // or the window handle is in flux. Screenshots are nice-to-have; never
            // let them fail the actual test assertion.
            try { SaveStep(Capture(s), testName, stepName); }
            catch { /* best effort */ }
        });

    /// <summary>
    /// Click the hamburger to open the main menu. The "MAIN" Text click bubbles
    /// to the parent StackPanel's ShowMenu handler, but occasionally the click
    /// doesn't register, so retry until "Advanced Settings" is visible.
    /// </summary>
    public static void OpenMainMenu(AppiumSession s) => Trace.Time("OpenMainMenu", () => OpenMainMenuCore(s));

    private static void OpenMainMenuCore(AppiumSession s)
    {
        // Resolve the hamburger Text ONCE; the menu open animates a slide but the
        // Text peer itself doesn't change identity. Saves a UIA tree walk per retry.
        var hamburger = s.Driver.FindElements(By.XPath("//Text[@Name='MAIN']"));
        if (hamburger.Count == 0)
            throw new TimeoutException("OpenMainMenu: 'MAIN' hamburger text not in UIA tree");

        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            try { hamburger[0].Click(); } catch { /* transiently stale */ }

            // Quick probe -- don't wait full timeout per attempt.
            var probe = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < probe)
            {
                if (s.Driver.FindElements(By.XPath("//*[@Name='Advanced Settings']")).Count > 0) return;
                Thread.Sleep(40);
            }
        }
        throw new TimeoutException("OpenMainMenu: 'Advanced Settings' never appeared after repeated hamburger clicks");
    }

    public static IWebElement WindowElement(AppiumSession s) =>
        Trace.Time("WindowElement", () =>
        {
            try { return s.Driver.FindElement(MobileBy.AccessibilityId("MainUI")); }
            catch (NoSuchElementException) { return s.Driver.FindElement(By.XPath("/*[1]")); }
        });

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

    public static IWebElement WaitFor(AppiumSession s, By by, int timeoutMs = 4000)
    {
        return Trace.Time($"WaitFor({by})", () =>
        {
            // FindElements (note the 's') returns an empty list on miss instead of
            // throwing NoSuchElementException. The throw across the WinAppDriver
            // HTTP boundary costs ~500ms; FindElements is ~150-300ms per try.
            // This shaved avg WaitFor cost roughly in half (533ms -> ~250ms).
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var els = s.Driver.FindElements(by);
                    if (els.Count > 0 && els[0].Displayed) return els[0];
                }
                catch (StaleElementReferenceException) { /* retry */ }
                Thread.Sleep(40);
            }
            throw new TimeoutException($"Timed out waiting for {by} after {timeoutMs}ms");
        });
    }

    public static IWebElement WaitForId(AppiumSession s, string id, int timeoutMs = 4000) =>
        Trace.Time($"WaitForId({id})",
            () => WaitFor(s, By.XPath($"//*[@AutomationId='{id}']"), timeoutMs));

    /// <summary>
    /// Robust click: try element.Click() first, fall back to an absolute-coord
    /// touch tap at viewport coords. WinAppDriver only supports pen/touch in
    /// W3C Actions (mouse is rejected with "Currently only pen and touch pointer
    /// input source types are supported"). Touch reliably clicks WPF elements
    /// that don't expose a UIA Invoke pattern.
    ///
    /// We do NOT call element.Click() first. WinAppDriver synthesises an Invoke
    /// for WPF Custom controls whose UIA peer happens to expose one even when
    /// no real handler is wired, so Click() can return success without firing
    /// the WPF MouseUp / Touch event that the handler is bound to.
    /// </summary>
    /// <summary>
    /// Synthesize a W3C touch tap at the element's centre. Skips element.Click()
    /// entirely. Useful when Click()'s ~1s round-trip overhead is the bottleneck
    /// (e.g. driving sort headers in tight loops); use ClickAt() when you want
    /// the Click()-first behaviour for elements that need UIA Invoke routing.
    /// </summary>
    /// <summary>
    /// Press at the element's centre, drag by (dx, dy) viewport pixels, release.
    /// Uses W3C Touch (WinAppDriver rejects Mouse pointer with "only pen and
    /// touch supported"). WPF promotes TouchDown -> MouseLeftButtonDown, which
    /// is what the WPF window's Window_MouseDown / DragMove handler hooks.
    /// </summary>
    public static void DragBy(AppiumSession s, IWebElement el, int dx, int dy)
    {
        Trace.Time($"DragBy(dx={dx},dy={dy})", () =>
        {
            var loc = el.Location;
            var size = el.Size;
            int cx = loc.X + (size.Width / 2);
            int cy = loc.Y + (size.Height / 2);

            var touch = new OpenQA.Selenium.Interactions.PointerInputDevice(
                OpenQA.Selenium.Interactions.PointerKind.Touch, "drag-touch");
            var seq = new OpenQA.Selenium.Interactions.ActionSequence(touch, 0);
            seq.AddAction(touch.CreatePointerMove(
                OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, cx, cy, TimeSpan.Zero));
            seq.AddAction(touch.CreatePointerDown(OpenQA.Selenium.Interactions.MouseButton.Touch));
            seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(80)));
            seq.AddAction(touch.CreatePointerMove(
                OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, cx + dx, cy + dy, TimeSpan.FromMilliseconds(200)));
            seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(80)));
            seq.AddAction(touch.CreatePointerUp(OpenQA.Selenium.Interactions.MouseButton.Touch));
            ((OpenQA.Selenium.IActionExecutor)s.Driver).PerformActions(
                new List<OpenQA.Selenium.Interactions.ActionSequence> { seq });
        });
    }

    public static void TouchTap(AppiumSession s, IWebElement el)
    {
        Trace.Time("TouchTap", () =>
        {
            var loc = el.Location;
            var size = el.Size;
            int cx = loc.X + (size.Width / 2);
            int cy = loc.Y + (size.Height / 2);

            var touch = new OpenQA.Selenium.Interactions.PointerInputDevice(
                OpenQA.Selenium.Interactions.PointerKind.Touch, "touch-tap");
            var seq = new OpenQA.Selenium.Interactions.ActionSequence(touch, 0);
            seq.AddAction(touch.CreatePointerMove(
                OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, cx, cy, TimeSpan.Zero));
            seq.AddAction(touch.CreatePointerDown(OpenQA.Selenium.Interactions.MouseButton.Touch));
            seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(20)));
            seq.AddAction(touch.CreatePointerUp(OpenQA.Selenium.Interactions.MouseButton.Touch));
            ((OpenQA.Selenium.IActionExecutor)s.Driver).PerformActions(
                new List<OpenQA.Selenium.Interactions.ActionSequence> { seq });
        });
    }

    public static void ClickAt(AppiumSession s, IWebElement el) =>
        Trace.Time("ClickAt", () => ClickAtCore(s, el));

    private static void ClickAtCore(AppiumSession s, IWebElement el)
    {
        // Try element.Click() first; if WinAppDriver rejects as not interactable,
        // fall back to a viewport-coord touch tap at the element's centre.
        try
        {
            el.Click();
            return;
        }
        catch (OpenQA.Selenium.ElementNotInteractableException)
        {
            // fall through
        }

        var loc = el.Location;
        var size = el.Size;
        int cx = loc.X + (size.Width / 2);
        int cy = loc.Y + (size.Height / 2);

        var touch = new OpenQA.Selenium.Interactions.PointerInputDevice(
            OpenQA.Selenium.Interactions.PointerKind.Touch, "touch-click");
        var seq = new OpenQA.Selenium.Interactions.ActionSequence(touch, 0);
        seq.AddAction(touch.CreatePointerMove(
            OpenQA.Selenium.Interactions.CoordinateOrigin.Viewport, cx, cy, TimeSpan.Zero));
        seq.AddAction(touch.CreatePointerDown(OpenQA.Selenium.Interactions.MouseButton.Touch));
        seq.AddAction(touch.CreatePause(TimeSpan.FromMilliseconds(40)));
        seq.AddAction(touch.CreatePointerUp(OpenQA.Selenium.Interactions.MouseButton.Touch));
        ((OpenQA.Selenium.IActionExecutor)s.Driver).PerformActions(
            new List<OpenQA.Selenium.Interactions.ActionSequence> { seq });
    }

    /// <summary>
    /// Find the IdentityItem Custom element whose row contains the given identity
    /// name. Lets tests work irrespective of sort order on the landing screen.
    /// </summary>
    public static IWebElement IdentityRow(AppiumSession s, string identityName) =>
        Trace.Time($"IdentityRow({identityName})", () =>
            // Brief WaitFor instead of immediate FindElement: the Text peer can
            // surface in the UIA tree a few frames before the parent Custom is
            // fully attached, which races OpenIdentityDetails callers that did
            // a WaitFor on the Text right before calling us.
            WaitFor(s, By.XPath(
                $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]"),
                timeoutMs: 2000));

    /// <summary>Open the identity-details screen by clicking the row whose name matches.</summary>
    public static void OpenIdentityDetails(AppiumSession s, string identityName) =>
        Trace.Time($"OpenIdentityDetails({identityName})",
            () => ClickAt(s, IdentityRow(s, identityName)));

    /// <summary>
    /// Close the identity-details overlay, returning to the landing list.
    /// Used by shared-session tests to reset between cases. Idempotent: silently
    /// no-ops if details isn't currently open.
    /// </summary>
    /// <summary>
    /// Dismiss the bottom-of-window error / info blurb by clicking its X.
    /// No-op if the blurb is not currently shown.
    /// </summary>
    public static void DismissBlurb(AppiumSession s) =>
        Trace.Time("DismissBlurb", () =>
        {
            var x = s.Driver.FindElements(By.XPath("//*[@AutomationId='BlurbClose']"));
            if (x.Count == 0) return;
            ClickAt(s, x[0]);
            // Wait briefly for the blurb to slide out.
            var deadline = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < deadline)
            {
                if (s.Driver.FindElements(By.XPath("//*[@AutomationId='BlurbClose']")).Count == 0) return;
                Thread.Sleep(30);
            }
        });

    public static void CloseIdentityDetails(AppiumSession s) =>
        Trace.Time("CloseIdentityDetails", () =>
        {
            var closeButtons = s.Driver.FindElements(By.XPath("//*[@AutomationId='CloseDetailsButton']"));
            if (closeButtons.Count == 0) return; // already on landing -- no-op
            ClickAt(s, closeButtons[0]);
            // Poll until the close button disappears from the tree (= overlay
            // closed). 600ms ceiling -- the close animation is ~300ms in WPF
            // but observed trace shows 700-1700ms in practice (UIA tree refresh
            // is the long pole). 600ms is the sweet spot: aborts a stuck close
            // fast without false-positive timing out on a normal one.
            var deadline = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < deadline)
            {
                if (s.Driver.FindElements(By.XPath("//*[@AutomationId='CloseDetailsButton']")).Count == 0) return;
                Thread.Sleep(30);
            }
        });

    public static SettingsTask VerifyPng(byte[] png, [CallerMemberName] string? testName = null)
    {
        try
        {
            var dir = Path.Combine(RepoRoot(), "UITests", "TestResults", "screenshots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{testName}.png"), png);
        }
        catch { /* best effort */ }

        var task = Verify(png, "png");
        if (Environment.GetEnvironmentVariable("ZDEW_AUTO_VERIFY") == "1")
        {
            task = task.AutoVerify();
        }
        return task;
    }
}
