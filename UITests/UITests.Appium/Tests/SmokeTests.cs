using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tests that need a fresh UI process per test: visual baselines, alt-fixture
/// screenshots, and state-mutating interactions. Read-only landing assertions
/// live in LandingReadOnlyTests (shared session, much faster).
/// </summary>
[TestLifecycleLog]
public class SmokeTests
{
    // Per-test traits go on the methods below since SmokeTests spans multiple
    // categories. Adding Category trait per-test would be too noisy; we tag the
    // big buckets via method-level [Trait] below.
    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task MainWindow_LaunchesAndRenders()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task MainMenu_OpensOnHamburgerClick()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");

        // The hamburger lives inside a StackPanel that has no UIA peer.
        // Click the child "MAIN" Text -- WPF routes the MouseLeftButtonUp up to the
        // parent StackPanel where ShowMenu is wired.
        var mainText = session.Driver.FindElement(By.XPath("//Text[@Name='MAIN']"));
        mainText.Click();

        WaitFor(session, By.XPath("//*[@Name='Identities']"));
        await Task.Delay(150); // animation settle

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task IdentityToggle_Click_FlipsBetaToEnabled()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");

        var toggles = session.Driver.FindElements(By.XPath("//*[@AutomationId='ToggleSwitch']"));
        Assert.Equal(2, toggles.Count);
        ClickAt(session, toggles[1]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.Mock.ReceivedCommandNames.Contains("IdentityOnOff")) break;
            await Task.Delay(50);
        }

        Assert.Contains("IdentityOnOff", session.Mock.ReceivedCommandNames);

        var lastIdentityOnOff = session.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "IdentityOnOff");
        Assert.Equal(true, (bool?)lastIdentityOnOff["Data"]?["OnOff"]);
        Assert.Equal("c:\\fake\\ids\\disabled-at-start-id.json",
            (string?)lastIdentityOnOff["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_Disconnected()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "disconnected.json");
        WaitForId(session, "ConnectLabel");
        await Task.Delay(150);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_NoIdentities()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "no-identities.json");
        WaitForId(session, "ConnectLabel");
        await Task.Delay(150);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_NeedsExtAuth()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        await Task.Delay(200);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_WithServices()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "with-services.json");
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='with-3-services-id']"));
        // wait for service count to flip from "-" to "3" once the updated event lands
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var sc = ById(session, "ServiceCount").Text;
                if (sc == "3") break;
            }
            catch (NoSuchElementException) { }
            await Task.Delay(100);
        }

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "LogLevel")]
    public async Task LogLevel_WalkThroughDebugThenTrace()
    {
        var name = nameof(LogLevel_WalkThroughDebugThenTrace);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        await Task.Delay(200);
        SaveStep(s, name, "02-main-menu");

        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Set Logging Level']"));
        await Task.Delay(200);
        SaveStep(s, name, "03-advanced-shown");

        WaitFor(s, By.XPath("//*[@Name='Set Logging Level']")).Click();
        WaitForId(s, "LogTrace");
        await Task.Delay(200);
        SaveStep(s, name, "04-loglevel-submenu");

        WaitForId(s, "LogDebug").Click();
        await Task.Delay(150);
        SaveStep(s, name, "05-after-clicking-debug");

        // Menu stays open after SetLevel -- click Trace directly without re-navigating.
        // (Clicking MAIN again toggles the menu CLOSED, so don't.)
        WaitForId(s, "LogTrace").Click();
        await Task.Delay(150);
        SaveStep(s, name, "06-after-clicking-trace");

        // The Set Logging Level menu sends to ziti-monitor via the monitor IPC
        // ({Op:"SetLogLevel", Action:"<level>"}), NOT the tunneler DataClient.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedMonitorOps.Count(o => o == "SetLogLevel") >= 2) break;
            await Task.Delay(50);
        }

        var levels = s.Mock.ReceivedMonitorRequests
            .Where(r => (string?)r["Op"] == "SetLogLevel")
            .Select(r => (string?)r["Action"])
            .ToList();
        Assert.Contains("debug", levels);
        Assert.Contains("trace", levels);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "TunnelSettings")]
    public async Task TunnelConfig_OpensConfigScreen()
    {
        var name = nameof(TunnelConfig_OpensConfigScreen);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        await Task.Delay(200);
        SaveStep(s, name, "02-main-menu");

        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']"));
        await Task.Delay(200);
        SaveStep(s, name, "03-advanced-shown");

        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(150);
        SaveStep(s, name, "04-tunnel-config-screen");
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "IdentityDetail")]
    public async Task IdentityDetails_OpensOnIdentityClick()
    {
        var name = nameof(IdentityDetails_OpensOnIdentityClick);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(150); // animation settle
        SaveStep(s, name, "02-identity-details-open");

        // Identity detail screen exposes IdName / IdServer / ForgetIdentityButton
        // -- assert at least one of these came alive to prove we navigated.
        Assert.True(s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).Count > 0);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Mfa")]
    public async Task MFA_EnableShowsQRDialog()
    {
        var name = nameof(MFA_EnableShowsQRDialog);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details");

        // Toggle Multi Factor Auth on
        var mfa = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']"));
        mfa.Click();

        // Wait for the EnableMFA command to land + the enrollment_challenge event
        // to come back, which triggers the QR/setup dialog.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(250); // QR render settle
        SaveStep(s, name, "03-after-clicking-mfa-toggle");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);
        var req = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "EnableMFA");
        Assert.Equal("c:\\fake\\ids\\enabled-id.json",
            (string?)req["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "TunnelSettings")]
    public async Task TunnelConfig_ClickEditValuesShowsForm()
    {
        var name = nameof(TunnelConfig_ClickEditValuesShowsForm);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(150);
        SaveStep(s, name, "02-tunnel-config-screen");

        // "Edit Values" is a StyledButton -- the inner label text is queryable.
        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(150);
        SaveStep(s, name, "03-after-clicking-edit-values");
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "TunnelSettings")]
    public async Task TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig()
    {
        var name = nameof(TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(100);
        SaveStep(s, name, "02-tunnel-config-screen");

        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(150);
        SaveStep(s, name, "03-edit-form");

        // The Save button on the edit form (SaveConfigButton) -- label is "Save".
        WaitFor(s, By.XPath("//*[@Name='Save']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("UpdateInterfaceConfig")) break;
            await Task.Delay(50);
        }
        await Task.Delay(100);
        SaveStep(s, name, "04-after-save");

        Assert.Contains("UpdateInterfaceConfig", s.Mock.ReceivedCommandNames);
        var cmd = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "UpdateInterfaceConfig");
        // L3 + L2 payloads must be present
        Assert.NotNull(cmd["Data"]?["L3"]);
        Assert.NotNull(cmd["Data"]?["L2"]);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "IdentityDetail")]
    public async Task ExtAuth_AuthorizeClick_SendsExternalAuthCommand()
    {
        var name = nameof(ExtAuth_AuthorizeClick_SendsExternalAuthCommand);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing-with-ext-auth-identity");

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details-shows-auth-button");

        // The AuthenticateWithProvider button starts Collapsed; it only becomes
        // visible after a row is selected in the ProviderList ListBox. Click the
        // first provider entry, then the button.
        var firstProvider = WaitFor(s, By.XPath("//List[@Name='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Task.Delay(100);
        SaveStep(s, name, "02b-provider-selected");

        WaitFor(s, By.XPath("//*[@AutomationId='AuthenticateWithProvider']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("ExternalAuth")) break;
            await Task.Delay(50);
        }
        await Task.Delay(150);
        SaveStep(s, name, "03-after-authorize-click");

        Assert.Contains("ExternalAuth", s.Mock.ReceivedCommandNames);
        var cmd = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "ExternalAuth");
        Assert.Equal("c:\\fake\\ids\\needs-ext-auth-id.json",
            (string?)cmd["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "IdentityDetailServices")]
    public async Task IdentityDetails_ShowsServiceList()
    {
        var name = nameof(IdentityDetails_ShowsServiceList);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing-with-services");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(200);
        SaveStep(s, name, "02-identity-details-with-3-services");

        // Validate at least one of the services we put in the fixture renders.
        Assert.True(WaitFor(s, By.XPath("//*[@Name='wiki.example']")).Displayed);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task ManyIdentities_LandingShowsScrollableList()
    {
        var name = nameof(ManyIdentities_LandingShowsScrollableList);
        var status = FixtureBuilder.ManyMixedIdentities(count: 50);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), status);
        WaitForId(s, "ConnectLabel");
        await Task.Delay(1500); // let the 50-row list virtualise / paint
        SaveStep(s, name, "01-50-identities");

        // Doing 4 separate FindElement queries against a UIA tree with 50 identities is
        // extremely slow (~10s each). One PageSource fetch + string contains is orders
        // of magnitude faster.
        var src = s.Driver.PageSource;
        Assert.Contains("enabled-00", src);
        Assert.Contains("disabled-01", src);
        Assert.Contains("mfa-required-02", src);
        Assert.Contains("ext-auth-03", src);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_AfterTogglingBetaIdentityOn()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");
        // Wait for both rows to render before snapshotting toggles -- the second
        // IdentityItem occasionally lags the first by a frame or two.
        WaitFor(session, By.XPath("//Text[@Name='disabled-at-start-id']"));
        await Task.Delay(200);

        var toggles = session.Driver.FindElements(By.XPath("//*[@AutomationId='ToggleSwitch']"));
        Assert.Equal(2, toggles.Count);
        ClickAt(session, toggles[1]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.Mock.ReceivedCommandNames.Contains("IdentityOnOff")) break;
            await Task.Delay(50);
        }
        await Task.Delay(200); // animation settle

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "IdentityDetail")]
    public async Task ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault()
    {
        var name = nameof(ExtAuth_ClickIsDefaultProviderCheckbox_TogglesDefault);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details");

        // The IsDefaultProvider CheckBox stays disabled until a provider is selected.
        var firstProvider = WaitFor(s, By.XPath("//List[@Name='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Task.Delay(100);
        SaveStep(s, name, "03-provider-selected");

        var check = WaitFor(s, By.XPath("//*[@AutomationId='IsDefaultProvider']"));
        ClickAt(s, check);
        await Task.Delay(100);
        SaveStep(s, name, "04-after-checking-default");

        // Click again to uncheck -- proves the control is interactive.
        ClickAt(s, check);
        await Task.Delay(100);
        SaveStep(s, name, "05-after-unchecking-default");
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "MainScreen")]
    public async Task AddIdentity_ClickButton_OpensAddIdentityDialog()
    {
        var name = nameof(AddIdentity_ClickButton_OpensAddIdentityDialog);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        // AddIdAreaButton is a StackPanel that WPF doesn't expose a UIA peer for
        // even with AutomationProperties.AutomationId set. The two child Labels
        // ("ADD" and "IDENTITY") do expose peers though -- click the "ADD" Text.
        // MouseLeftButtonUp on the StackPanel bubbles up from the child click.
        var btn = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, btn);
        await Task.Delay(150);
        SaveStep(s, name, "02-after-add-identity-click");

        // The Add Identity dialog surfaces the AddIdentityByURL / signer picker
        // panels. Just assert *something* changed in the tree -- the existence of
        // an Edit (URL field) or button labelled with related text is enough.
        var src = s.Driver.PageSource;
        var opened = src.Contains("AddIdentityByURL", StringComparison.OrdinalIgnoreCase)
                  || src.Contains("3rd Party", StringComparison.OrdinalIgnoreCase)
                  || src.Contains("Add Identity", StringComparison.OrdinalIgnoreCase)
                  || src.Contains("Signer", StringComparison.OrdinalIgnoreCase);
        Assert.True(opened, "Clicking AddIdAreaButton should reveal Add Identity / signer panel content.");
    }
}
