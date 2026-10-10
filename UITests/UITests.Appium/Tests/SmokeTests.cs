using System.Collections.ObjectModel;
using System.Drawing;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tests that need their own UI process: screenshot baselines, alternate fixtures, and anything that changes state.
/// </summary>
[TestLifecycleLog]
public class SmokeTests
{
    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainWindow_LaunchesAndRenders()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(),
            Fixture("landing-status.json"), UiLogPath(nameof(MainWindow_LaunchesAndRenders)));
        WaitForId(session, "ConnectLabel");
        await VerifyPng(Capture(session));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainMenu_OpensOnHamburgerClick()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(),
            Fixture("landing-status.json"), UiLogPath(nameof(MainMenu_OpensOnHamburgerClick)));
        WaitForId(session, "ConnectLabel");
        OpenMainMenu(session);
        WaitFor(session, By.XPath("//*[@Name='Identities']"));
        await VerifyPng(Capture(session));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task AddIdentityOffersJwtAndUrl()
    {
        string name = nameof(AddIdentityOffersJwtAndUrl);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitForId(s, "ConnectLabel");
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the click bubbles up to it.
        ClickUntil(s, By.XPath("//Text[@Name='ADD']"), By.XPath("//MenuItem"));
        string[] items = s.Driver.FindElements(By.XPath("//MenuItem")).Select(item => item.GetAttribute("Name")).ToArray();
        Assert.Equal(new[] { "With JWT", "With URL" }, items);
        await VerifyPng(CaptureWithPopups(s));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_Disconnected()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("disconnected.json"), UiLogPath(nameof(Visual_Disconnected)));
        WaitForId(session, "ConnectLabel");
        await VerifyPng(Capture(session));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_NoIdentities()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("no-identities.json"), UiLogPath(nameof(Visual_NoIdentities)));
        WaitForId(session, "ConnectLabel");
        await VerifyPng(Capture(session));
    }

    // PrintWindow draws a window hidden behind the taskbar just fine, so screenshots can't catch placement.
    [Fact(Timeout = 60000)]
    [Trait("Category", "Placement")]
    public async Task DockedWindowStaysOnScreen()
    {
        string name = nameof(DockedWindowStaysOnScreen);
        // 5 rows nearly fill IdList.MaxHeight, so the main view is about as tall as it gets, and every row stays in
        // view whatever the persisted sort order, which OpenIdentityDetails needs.
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(),
            FixtureBuilder.ManyMixedIdentities(count: 5), UiLogPath(name));
        WaitFor(s, By.XPath("//Text[@Name='enabled-00']"));
        Rectangle landing = AssertOnScreen(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-00");
        Rectangle details = AssertOnScreen(s, name, "02-identity-details");
        Assert.True(details.Width > landing.Width, $"identity details {details} should be wider than landing {landing}");
        Assert.Equal(landing.Left, details.Left);

        CloseIdentityDetails(s);
        Rectangle closed = AssertOnScreen(s, name, "03-details-closed");
        Assert.Equal(landing, closed);
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "Placement")]
    public async Task DockedWelcomeStaysOnScreen()
    {
        string name = nameof(DockedWelcomeStaysOnScreen);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("no-identities.json"),
            UiLogPath(name));
        WaitFor(s, By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']"));
        AssertOnScreen(s, name, "01-welcome");
    }

    /// <summary>
    /// Saves the window as a step, then asserts it sits wholly inside the work area: not under the taskbar, not off an
    /// edge.
    /// </summary>
    private static Rectangle AssertOnScreen(AppiumSession s, string testName, string stepName)
    {
        SaveStep(s, testName, stepName);
        Rectangle window = s.WindowBounds();
        Rectangle workArea = AppiumSession.WorkArea();
        Assert.True(workArea.Contains(window), $"{stepName}: window {window} is not inside the work area {workArea}");
        return window;
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    public async Task ManyIdentities_LandingShowsScrollableList()
    {
        string name = nameof(ManyIdentities_LandingShowsScrollableList);
        // 25 rows is far past the 4-5 visible.
        const int count = 25;
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(),
            FixtureBuilder.ManyMixedIdentities(count), UiLogPath(name));
        WaitUntil(s, $"the list holds {count} rows", TimeSpan.FromSeconds(10),
            () => IntegrationHelpers.IdentityRowCount(s) == count);
        SaveStep(s, name, "01-many-identities");
        ReadOnlyCollection<AppiumElement> rows =s.Driver.FindElements(By.XPath("//Custom[@ClassName='IdentityItem']"));
        // A scrolled-out IdentityItem still reports displayed, so overflow shows as a row starting below the window.
        // Element locations are relative to the window.
        int windowHeight = s.WindowBounds().Height;
        Assert.Contains(rows, row => row.Location.Y >= windowHeight);
    }
}
