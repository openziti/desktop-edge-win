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

    public static void SaveStep(AppiumSession s, string testName, string stepName) =>
        SaveStep(Capture(s), testName, stepName);

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

    public static IWebElement WaitFor(AppiumSession s, By by, int timeoutMs = 8000)
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
            Thread.Sleep(75);
        }
        throw new TimeoutException($"Timed out waiting for {by} after {timeoutMs}ms", last);
    }

    public static IWebElement WaitForId(AppiumSession s, string id, int timeoutMs = 8000) =>
        WaitFor(s, By.XPath($"//*[@AutomationId='{id}']"), timeoutMs);

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
