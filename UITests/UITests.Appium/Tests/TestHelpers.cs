using System.Runtime.CompilerServices;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

public static class TestHelpers
{
    public static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static string DefaultExePath() =>
        Path.Combine(RepoRoot(), "DesktopEdge", "bin", "Debug", "ZitiDesktopEdge.exe");

    public static string FixturesDir() =>
        Path.Combine(AppContext.BaseDirectory, "MockIpc", "Fixtures");

    public static byte[] ElementScreenshot(IWebElement el) =>
        ((ITakesScreenshot)el).GetScreenshot().AsByteArray;

    /// <summary>
    /// Capture the entire ZDEW window as a PNG byte array.
    /// </summary>
    public static byte[] Capture(AppiumSession s) =>
        ElementScreenshot(WindowElement(s));

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

    public static void SaveStep(AppiumSession s, string testName, string stepName)
    {
        // Capture occasionally throws WebDriverException when an overlay opens or
        // the window handle is in flux. Screenshots are nice-to-have; never let
        // them fail the actual test assertion.
        try { SaveStep(Capture(s), testName, stepName); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Click the hamburger to open the main menu. The "MAIN" Text click bubbles
    /// to the parent StackPanel's ShowMenu handler, but occasionally the click
    /// doesn't register, so retry until "Advanced Settings" is visible.
    /// </summary>
    public static void OpenMainMenu(AppiumSession s)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                s.Driver.FindElement(By.XPath("//Text[@Name='MAIN']")).Click();
            }
            catch { /* element might be transiently unavailable */ }

            // Quick probe -- don't wait full timeout per attempt
            var probe = DateTime.UtcNow.AddMilliseconds(800);
            while (DateTime.UtcNow < probe)
            {
                if (s.Driver.FindElements(By.XPath("//*[@Name='Advanced Settings']")).Count > 0) return;
                Thread.Sleep(75);
            }
        }
        throw new TimeoutException("OpenMainMenu: 'Advanced Settings' never appeared after repeated hamburger clicks");
    }

    public static IWebElement WindowElement(AppiumSession s)
    {
        try { return s.Driver.FindElement(MobileBy.AccessibilityId("MainUI")); }
        catch (NoSuchElementException) { return s.Driver.FindElement(By.XPath("/*[1]")); }
    }

    public static IWebElement ById(AppiumSession s, string id) =>
        s.Driver.FindElement(By.XPath($"//*[@AutomationId='{id}']"));

    /// <summary>Element text or empty string if the element isn't in the tree.</summary>
    public static string TryGetTextById(AppiumSession s, string id)
    {
        try { return ById(s, id).Text ?? ""; }
        catch (NoSuchElementException) { return ""; }
    }

    public static IWebElement WaitFor(AppiumSession s, By by, int timeoutMs = 4000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var el = s.Driver.FindElement(by);
                if (el.Displayed) return el;
            }
            catch (NoSuchElementException ex) { last = ex; }
            catch (StaleElementReferenceException ex) { last = ex; }
            Thread.Sleep(40);
        }
        throw new TimeoutException($"Timed out waiting for {by} after {timeoutMs}ms", last);
    }

    public static IWebElement WaitForId(AppiumSession s, string id, int timeoutMs = 4000) =>
        WaitFor(s, By.XPath($"//*[@AutomationId='{id}']"), timeoutMs);

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
    public static void ClickAt(AppiumSession s, IWebElement el)
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
        // Brief WaitFor instead of immediate FindElement: the Text peer can
        // surface in the UIA tree a few frames before the parent Custom is
        // fully attached, which races OpenIdentityDetails callers that did
        // a WaitFor on the Text right before calling us.
        WaitFor(s, By.XPath(
            $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]"),
            timeoutMs: 2000);

    /// <summary>Open the identity-details screen by clicking the row whose name matches.</summary>
    public static void OpenIdentityDetails(AppiumSession s, string identityName)
    {
        ClickAt(s, IdentityRow(s, identityName));
    }

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
