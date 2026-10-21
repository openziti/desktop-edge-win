using System.Drawing;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

public class SmokeTests
{
    public SmokeTests(ITestOutputHelper output)
    {
        Step.Use(output);
    }

    private static readonly By LandingRow = By.XPath("//Text[@Name='enabled-id']");

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainWindow_LaunchesAndRenders()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(),
            Fixture("landing-status.json"), UiLogPath(nameof(MainWindow_LaunchesAndRenders)));
        WaitFor(session, LandingRow);
        await VerifyPng(Capture(session), nameof(MainWindow_LaunchesAndRenders));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainMenu_OpensOnHamburgerClick()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(),
            Fixture("landing-status.json"), UiLogPath(nameof(MainMenu_OpensOnHamburgerClick)));
        WaitFor(session, LandingRow);
        OpenMainMenu(session);
        WaitFor(session, By.XPath("//*[@Name='Identities']"));
        await VerifyPng(Capture(session), nameof(MainMenu_OpensOnHamburgerClick));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task AddIdentityOffersJwtAndUrl()
    {
        string name = nameof(AddIdentityOffersJwtAndUrl);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitFor(s, LandingRow);
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the click bubbles up to it.
        ClickUntil(s, By.XPath("//Text[@Name='ADD']"), By.XPath("//MenuItem"));
        string[] items = s.Driver.FindElements(By.XPath("//MenuItem")).Select(item => item.GetAttribute("Name")).ToArray();
        Assert.Equal(new[] { "With JWT", "With URL" }, items);
        await VerifyPng(CaptureWithPopups(s), nameof(AddIdentityOffersJwtAndUrl));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_NoIdentities()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("no-identities.json"), UiLogPath(nameof(Visual_NoIdentities)));
        WaitFor(session, WelcomeClose);
        await VerifyPng(Capture(session), nameof(Visual_NoIdentities));
    }

    // PrintWindow draws a window hidden behind the taskbar just fine, so screenshots can't catch placement.
    [Fact(Timeout = 60000)]
    [Trait("Category", "Placement")]
    public async Task DockedWindowStaysOnScreen()
    {
        string name = nameof(DockedWindowStaysOnScreen);
        // 5 rows nearly fill IdList.MaxHeight yet stay in view in any sort, as OpenIdentityDetails needs.
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
        WaitFor(s, By.XPath("//*[@AutomationId='IdListScroller' and @VerticallyScrollable='True']"));
    }
}
