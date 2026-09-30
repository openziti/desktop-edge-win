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
    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainWindow_LaunchesAndRenders()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(session, "ConnectLabel");

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(TimerMasked(session, png));
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task MainMenu_OpensOnHamburgerClick()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(session, "ConnectLabel");

        OpenMainMenu(session);
        WaitFor(session, By.XPath("//*[@Name='Identities']"));
        await Trace.Settle(350); // animation settle

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_Disconnected()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("disconnected.json"));
        WaitForId(session, "ConnectLabel");
        await Trace.Settle(350);

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(TimerMasked(session, png));
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_NoIdentities()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("no-identities.json"));
        WaitForId(session, "ConnectLabel");
        await Trace.Settle(350);

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_NeedsExtAuth()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"));
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        await Trace.Settle(350);

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(TimerMasked(session, png));
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task Visual_WithServices()
    {
        await using AppiumSession session = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("with-services.json"));
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='with-3-services-id']"));
        // the service count reads "-" until the status event's services render
        WaitUntil(session, "the service count reads 3", TimeSpan.FromSeconds(5),
            () => TryGetTextById(session, "ServiceCount") == "3");

        byte[] png = Capture(session);
        Assert.NotEmpty(png);
        await VerifyPng(TimerMasked(session, png));
    }

    // Five menu steps, two screen baselines, form input and a Save click, hence the limit.
    [Fact(Timeout = 40000)]
    [Trait("Category", "TunnelSettings")]
    [Trait("Category", "Screenshots")]
    public async Task TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig()
    {
        string name = nameof(TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        ClickUntil(s, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Tunnel Config']"));
        ClickUntil(s, By.XPath("//*[@Name='Tunnel Config']"), By.XPath("//*[@Name='Edit Values']"));
        await Trace.Settle(350);
        SaveStep(s, name, "02-tunnel-config-screen");
        await VerifyScreen(Capture(s), "tunnel-config");

        // TunIpv4 from landing-status.json
        IWebElement ip = WaitFor(s, By.XPath("//*[@AutomationId='ConfigIp']//*[@AutomationId='MainEdit']"));
        Assert.Equal("100.150.0.0", ip.Text);

        // Edit Values is a StyledButton, found by its label text.
        ClickUntil(s, By.XPath("//*[@Name='Edit Values']"), By.XPath("//*[@Name='Save']"));
        await Trace.Settle(350);
        await VerifyScreen(Capture(s), "edit-form");

        IWebElement ipBox = WaitForId(s, "ConfigIpNew");
        // the form starts from the current values
        Assert.Equal("100.150.0.0", ipBox.Text);
        ipBox.Clear();
        ipBox.SendKeys("100.120.0.0");
        IWebElement pageSizeBox = WaitForId(s, "ConfigePageSizeNew");
        pageSizeBox.Clear();
        pageSizeBox.SendKeys("100");
        SaveStep(s, name, "03-edit-form");

        // SaveConfigButton, labeled "Save".
        WaitFor(s, By.XPath("//*[@Name='Save']")).Click();

        JObject cmd = WaitForCommand(s, "UpdateInterfaceConfig", 0);
        await Trace.Settle(350);
        SaveStep(s, name, "04-after-save");

        Assert.Equal("100.120.0.0", (string?)cmd["Data"]?["L3"]?["TunIPv4"]);
        Assert.Equal(100, (int?)cmd["Data"]?["L3"]?["ApiPageSize"]);
        Assert.NotNull(cmd["Data"]?["L2"]);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "IdentityDetail")]
    [Trait("Category", "Screenshots")]
    public async Task ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth()
    {
        // Authenticate With Provider opens a real browser, so the test injects ZET's post-login event instead.
        string name = nameof(ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing-with-ext-auth-identity");

        // The indicator has to be there first, or its absence later proves nothing.
        string srcBefore = s.Driver.PageSource;
        Assert.Contains("ExtAuthRequired", srcBefore);

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details-shows-auth-button");
        await VerifyScreen(Capture(s), "ext-auth-providers");

        // Select a provider but never click Authorize, which would open a browser.
        IWebElement firstProvider = WaitFor(s, By.XPath("//List[@AutomationId='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Trace.Settle(350);
        SaveStep(s, name, "03-provider-selected");

        s.Mock.PushExtAuthSuccess("c:\\fake\\ids\\needs-ext-auth-id.json");

        // A collapsed ExtAuthRequired image drops out of the UIA tree.
        WaitForGone(s, By.XPath("//*[@AutomationId='ExtAuthRequired']"));
        SaveStep(s, name, "04-after-simulated-success");
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "IdentityDetailServices")]
    [Trait("Category", "Screenshots")]
    public async Task IdentityDetails_ShowsServiceList()
    {
        string name = nameof(IdentityDetails_ShowsServiceList);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing-with-services");

        OpenIdentityDetails(s, "enabled-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details-with-3-services");

        Assert.True(WaitFor(s, By.XPath("//*[@Name='wiki.example']")).Displayed);
        string src = s.Driver.PageSource;
        Assert.Contains("prometheus.example", src);
        Assert.Contains("bastion.example", src);
        await VerifyScreen(Capture(s), "details");
    }

    // PrintWindow draws a window hidden behind the taskbar just fine, so screenshots can't catch placement.
    [Fact(Timeout = 20000)]
    [Trait("Category", "Placement")]
    public async Task DockedWindowStaysOnScreen()
    {
        string name = nameof(DockedWindowStaysOnScreen);
        // 5 rows nearly fill IdList.MaxHeight, so the main view is about as tall as it gets, and every row stays in
        // view whatever the persisted sort order, which OpenIdentityDetails needs.
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(),
            FixtureBuilder.ManyMixedIdentities(count: 5));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='enabled-00']"));
        await Trace.Settle(350);
        Rectangle landing = AssertOnScreen(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-00");
        await Trace.Settle(350);
        Rectangle details = AssertOnScreen(s, name, "02-identity-details");
        Assert.True(details.Width > landing.Width, $"identity details {details} should be wider than landing {landing}");
        Assert.Equal(landing.Left, details.Left);

        CloseIdentityDetails(s);
        await Trace.Settle(350);
        Rectangle closed = AssertOnScreen(s, name, "03-details-closed");
        Assert.Equal(landing, closed);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "Placement")]
    public async Task DockedWelcomeStaysOnScreen()
    {
        string name = nameof(DockedWelcomeStaysOnScreen);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("no-identities.json"));
        WaitFor(s, By.XPath("//*[@AutomationId='GetStartedScreen']//*[@AutomationId='CloseButton']"));
        await Trace.Settle(350);
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

    // 25-row UIA tree + virtualised ScrollViewer. PageSource cost dominates.
    [Fact(Timeout = 30000)]
    [Trait("Category", "MainScreen")]
    public async Task ManyIdentities_LandingShowsScrollableList()
    {
        string name = nameof(ManyIdentities_LandingShowsScrollableList);
        // 25 rows is still far past the 4-5 visible, so the list scrolls, at half the PageSource cost of 50 (about 10s).
        JObject status = FixtureBuilder.ManyMixedIdentities(count: 25);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), status);
        WaitForId(s, "ConnectLabel");
        // PageSource, not WaitFor: rows scrolled out of view report not displayed.
        string src = "";
        WaitUntil(s, "the list includes the last identity type", TimeSpan.FromSeconds(10),
            () => (src = s.Driver.PageSource).Contains("ext-auth-03"));
        SaveStep(s, name, "01-many-identities");

        Assert.Contains("enabled-00", src);
        Assert.Contains("disabled-01", src);
        Assert.Contains("mfa-required-02", src);
        Assert.Contains("ext-auth-03", src);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "IdentityDetail")]
    public async Task ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault()
    {
        string name = nameof(ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("needs-ext-auth.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        // The IsDefaultProvider CheckBox stays disabled until a provider is selected.
        IWebElement firstProvider = WaitFor(s, By.XPath("//List[@AutomationId='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Trace.Settle(350);
        SaveStep(s, name, "03-provider-selected");

        IWebElement check = WaitFor(s, By.XPath("//*[@AutomationId='IsDefaultProvider']"));
        // Relative to the start: DefaultProviders persists in the user's user.config between runs.
        bool startedChecked = check.Selected;
        ClickAt(s, check);
        await Trace.Settle(350);
        SaveStep(s, name, "04-after-checking-default");
        Assert.Equal(!startedChecked, check.Selected);

        ClickAt(s, check);
        await Trace.Settle(350);
        SaveStep(s, name, "05-after-unchecking-default");
        Assert.Equal(startedChecked, check.Selected);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "MainScreen")]
    public async Task AddIdentityOffersJwtAndUrl()
    {
        string name = nameof(AddIdentityOffersJwtAndUrl);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        // AddIdAreaButton's StackPanel has no UIA peer. Its "ADD" label does, and the click bubbles up to it.
        ClickUntil(s, By.XPath("//Text[@Name='ADD']"), By.XPath("//*[@Name='With JWT']"));
        WaitFor(s, By.XPath("//*[@Name='With URL']"));
        SaveStep(s, name, "02-add-identity-menu");
    }
}
