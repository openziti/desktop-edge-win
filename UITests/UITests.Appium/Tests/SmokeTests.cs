using System.Collections.ObjectModel;
using System.Drawing;
using Newtonsoft.Json.Linq;
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
    private static readonly By ExtAuthRequired = By.XPath("//*[@AutomationId='ExtAuthRequired']");
    private static readonly By FirstProvider = By.XPath("//List[@AutomationId='ProviderList']/ListItem[1]");

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
    public async Task AddIdentityOffersJwtAndUrl()
    {
        string name = nameof(AddIdentityOffersJwtAndUrl);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitForId(s, "ConnectLabel");
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the click bubbles up to it.
        ClickUntil(s, By.XPath("//Text[@Name='ADD']"), By.XPath("//MenuItem"));
        // The ContextMenu is a popup with its own window, which the window capture never draws, so no baseline.
        string[] items = s.Driver.FindElements(By.XPath("//MenuItem")).Select(item => item.GetAttribute("Name")).ToArray();
        Assert.Equal(new[] { "With JWT", "With URL" }, items);
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

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_NeedsExtAuth()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"), UiLogPath(nameof(Visual_NeedsExtAuth)));
        WaitFor(session, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        await VerifyPng(Capture(session));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_WithServices()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("with-services.json"), UiLogPath(nameof(Visual_WithServices)));
        WaitFor(session, By.XPath("//Text[@Name='with-3-services-id']"));
        // The service count reads "-" until the status event's services render.
        WaitUntil(session, "the service count reads 3", TimeSpan.FromSeconds(5),
            () => TextById(session, "ServiceCount") == "3");
        await VerifyPng(Capture(session));
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "TunnelSettings")]
    [Trait("Category", "Screenshots")]
    public async Task TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig()
    {
        string name = nameof(TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitForId(s, "ConnectLabel");
        OpenMainMenu(s);
        ClickUntil(s, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Tunnel Config']"));
        ClickUntil(s, By.XPath("//*[@Name='Tunnel Config']"), By.XPath("//*[@Name='Edit Values']"));
        await VerifyStep(Capture(s), name, "01-tunnel-config");
        // TunIpv4 from landing-status.json
        Assert.Equal("100.150.0.0", WaitFor(s, By.XPath("//*[@AutomationId='ConfigIp']//*[@AutomationId='MainEdit']")).Text);

        // Edit Values is a StyledButton, found by its label text.
        ClickUntil(s, By.XPath("//*[@Name='Edit Values']"), By.XPath("//*[@Name='Save']"));
        await VerifyStep(Capture(s), name, "02-edit-form");
        IWebElement ipBox = WaitForId(s, "ConfigIpNew");
        Assert.Equal("100.150.0.0", ipBox.Text);
        ipBox.Clear();
        ipBox.SendKeys("100.120.0.0");
        IWebElement pageSizeBox = WaitForId(s, "ConfigePageSizeNew");
        pageSizeBox.Clear();
        pageSizeBox.SendKeys("100");
        SaveStep(s, name, "03-values-typed");

        WaitFor(s, By.XPath("//*[@Name='Save']")).Click();
        JObject cmd = WaitForCommand(s, "UpdateInterfaceConfig", 0);
        Assert.Equal("100.120.0.0", (string?)cmd["Data"]?["L3"]?["TunIPv4"]);
        Assert.Equal(100, (int?)cmd["Data"]?["L3"]?["ApiPageSize"]);
        Assert.NotNull(cmd["Data"]?["L2"]);
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "IdentityDetail")]
    [Trait("Category", "Screenshots")]
    public async Task ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth()
    {
        // Authenticate With Provider opens a real browser, so the test injects ZET's post-login event instead.
        string name = nameof(ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"), UiLogPath(name));
        // The indicator has to be there first, or its absence later proves nothing.
        WaitFor(s, ExtAuthRequired);

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await VerifyStep(Capture(s), name, "01-ext-auth-providers");

        s.Mock.PushExtAuthSuccess("c:\\fake\\ids\\needs-ext-auth-id.json");
        // A collapsed ExtAuthRequired image drops out of the UIA tree.
        WaitForGone(s, ExtAuthRequired);
        SaveStep(s, name, "02-after-login-event");
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "IdentityDetailServices")]
    [Trait("Category", "Screenshots")]
    public async Task IdentityDetails_ShowsServiceList()
    {
        string name = nameof(IdentityDetails_ShowsServiceList);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitForId(s, "ConnectLabel");
        OpenIdentityDetails(s, "enabled-id");
        WaitFor(s, By.XPath("//*[@Name='wiki.example']"));
        await VerifyStep(Capture(s), name, "01-details");
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

    [Fact(Timeout = 60000)]
    [Trait("Category", "IdentityDetail")]
    public async Task ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault()
    {
        string name = nameof(ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"), UiLogPath(name));
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        OpenIdentityDetails(s, "needs-ext-auth-id");

        // The IsDefaultProvider CheckBox stays disabled until a provider is selected.
        ClickAt(s, WaitFor(s, FirstProvider));
        IWebElement check = WaitFor(s, By.XPath("//*[@AutomationId='IsDefaultProvider']"));
        // Relative to the start: DefaultProviders persists in the user's user.config between runs.
        bool startedChecked = check.Selected;
        ClickAt(s, check);
        WaitUntil(s, "the default provider checkbox flips", TimeSpan.FromSeconds(5),
            () => check.Selected != startedChecked);
        SaveStep(s, name, "01-after-first-click");

        ClickAt(s, check);
        WaitUntil(s, "the default provider checkbox flips back", TimeSpan.FromSeconds(5),
            () => check.Selected == startedChecked);
    }
}
