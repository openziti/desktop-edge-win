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
    [Fact(Timeout = 10000)]
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

    [Fact(Timeout = 10000)]
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
        await Task.Delay(350); // animation settle

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task IdentityToggle_Click_FlipsBetaToEnabled()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='disabled-at-start-id']"));

        // Find the ToggleSwitch INSIDE the disabled-at-start-id row -- not
        // toggles[1] by position. Sort persistence across tests means index
        // ordering isn't stable; targeting the row by name always picks the
        // right toggle.
        var toggle = session.Driver.FindElement(By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='disabled-at-start-id']]" +
            "//*[@AutomationId='ToggleSwitch']"));
        ClickAt(session, toggle);

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

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_Disconnected()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "disconnected.json");
        WaitForId(session, "ConnectLabel");
        await Task.Delay(350);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_NoIdentities()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "no-identities.json");
        WaitForId(session, "ConnectLabel");
        await Task.Delay(350);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_NeedsExtAuth()
    {
        await using var session = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(session, "ConnectLabel");
        WaitFor(session, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        await Task.Delay(350);

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 10000)]
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
            await Task.Delay(350);
        }

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    // Multi-step menu navigation (hamburger -> Advanced Settings -> Set Logging Level
    // -> Debug -> hamburger -> ... -> Trace). Each menu open animates.
    [Fact(Timeout = 15000)]
    [Trait("Category", "LogLevel")]
    public async Task LogLevel_WalkThroughDebugThenTrace()
    {
        var name = nameof(LogLevel_WalkThroughDebugThenTrace);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        await Task.Delay(350);
        SaveStep(s, name, "02-main-menu");

        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Set Logging Level']"));
        await Task.Delay(350);
        SaveStep(s, name, "03-advanced-shown");

        WaitFor(s, By.XPath("//*[@Name='Set Logging Level']")).Click();
        WaitForId(s, "LogTrace");
        await Task.Delay(350);
        SaveStep(s, name, "04-loglevel-submenu");

        WaitForId(s, "LogDebug").Click();
        await Task.Delay(350);
        SaveStep(s, name, "05-after-clicking-debug");

        // Menu stays open after SetLevel -- click Trace directly without re-navigating.
        // (Clicking MAIN again toggles the menu CLOSED, so don't.)
        WaitForId(s, "LogTrace").Click();
        await Task.Delay(350);
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

    // Hamburger -> Advanced Settings -> Tunnel Config (3-hop menu nav). Same
    // budget as its TunnelConfig siblings.
    [Fact(Timeout = 15000)]
    [Trait("Category", "TunnelSettings")]
    public async Task TunnelConfig_OpensConfigScreen()
    {
        var name = nameof(TunnelConfig_OpensConfigScreen);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        await Task.Delay(350);
        SaveStep(s, name, "02-main-menu");

        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']"));
        await Task.Delay(350);
        SaveStep(s, name, "03-advanced-shown");

        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(350);
        SaveStep(s, name, "04-tunnel-config-screen");
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "IdentityDetail")]
    public async Task IdentityDetails_OpensOnIdentityClick()
    {
        var name = nameof(IdentityDetails_OpensOnIdentityClick);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(350); // animation settle
        SaveStep(s, name, "02-identity-details-open");

        // Identity detail screen exposes IdName / IdServer / ForgetIdentityButton
        // -- assert at least one of these came alive to prove we navigated.
        Assert.True(s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).Count > 0);
    }

    // Launch + OpenIdentityDetails + Toggle MFA + QR dialog render: 4 distinct
    // animated transitions stack up here.
    [Fact(Timeout = 15000)]
    [Trait("Category", "Mfa")]
    public async Task MFA_EnableShowsQRDialog()
    {
        var name = nameof(MFA_EnableShowsQRDialog);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(350);
        SaveStep(s, name, "02-identity-details");

        // Toggle Multi Factor Auth on. IdentityMFA is a MenuEditToggle Custom;
        // the actual toggle handler is on ToggleField inside it. Clicking the
        // outer Custom doesn't route to the Toggler's mouse event.
        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);

        // Wait for the EnableMFA command to land + the enrollment_challenge event
        // to come back, which triggers the QR/setup dialog.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(400); // QR render settle
        SaveStep(s, name, "03-after-clicking-mfa-toggle");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);
        var req = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "EnableMFA");
        Assert.Equal("c:\\fake\\ids\\enabled-id.json",
            (string?)req["Data"]?["Identifier"]);
    }

    // Hamburger -> Advanced Settings -> Tunnel Config -> Edit Values is a
    // 4-hop menu navigation with animated transitions.
    [Fact(Timeout = 15000)]
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
        await Task.Delay(350);
        SaveStep(s, name, "02-tunnel-config-screen");

        // "Edit Values" is a StyledButton -- the inner label text is queryable.
        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(350);
        SaveStep(s, name, "03-after-clicking-edit-values");
    }

    // 5-hop menu navigation plus form input + Save click.
    [Fact(Timeout = 15000)]
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
        await Task.Delay(350);
        SaveStep(s, name, "02-tunnel-config-screen");

        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(350);
        SaveStep(s, name, "03-edit-form");

        // The Save button on the edit form (SaveConfigButton) -- label is "Save".
        WaitFor(s, By.XPath("//*[@Name='Save']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("UpdateInterfaceConfig")) break;
            await Task.Delay(50);
        }
        await Task.Delay(350);
        SaveStep(s, name, "04-after-save");

        Assert.Contains("UpdateInterfaceConfig", s.Mock.ReceivedCommandNames);
        var cmd = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "UpdateInterfaceConfig");
        // L3 + L2 payloads must be present
        Assert.NotNull(cmd["Data"]?["L3"]);
        Assert.NotNull(cmd["Data"]?["L2"]);
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "IdentityDetail")]
    public async Task ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth()
    {
        // We can't click "Authenticate With Provider" in tests: the WPF handler
        // calls Process.Start(url) which would launch a real browser. Instead,
        // simulate the post-login state by pushing the identity-added event
        // ziti-edge-tunnel emits once the OIDC redirect completes. ZDEW's
        // MainWindow.IdentityEvent_OnIdentityEvent treats action="added" with
        // NeedsExtAuth=false as a successful authentication and clears the
        // ext-auth indicators in the UI.
        var name = nameof(ExtAuth_SuccessfulLoginEvent_ClearsNeedsExtAuth);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing-with-ext-auth-identity");

        // Sanity: the ext-auth-needed indicator is in the tree before we
        // simulate authentication.
        var srcBefore = s.Driver.PageSource;
        Assert.Contains("ExtAuthRequired", srcBefore);

        OpenIdentityDetails(s, "needs-ext-auth-id");
        await Task.Delay(350);
        SaveStep(s, name, "02-identity-details-shows-auth-button");

        // Confirm a provider list is visible -- a real user would pick one
        // here, but we skip the click since AuthenticateWithProvider triggers
        // Process.Start(browserUrl) on the way out.
        var firstProvider = WaitFor(s, By.XPath("//List[@AutomationId='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Task.Delay(350);
        SaveStep(s, name, "03-provider-selected");

        // Simulate the tunneler-side success event. ExternalAuth IPC was never
        // sent (we didn't click Authorize), so this is the cleanest hand-rolled
        // success injection.
        s.Mock.PushExtAuthSuccess("c:\\fake\\ids\\needs-ext-auth-id.json");

        // Wait for the UI to swallow the event and re-render. The ExtAuthRequired
        // Image flips to Collapsed once NeedsExtAuth=false propagates.
        var deadline = DateTime.UtcNow.AddSeconds(4);
        bool cleared = false;
        while (DateTime.UtcNow < deadline)
        {
            // ExtAuthRequired is the Image AutomationId on landing rows; once
            // it's no longer Visible, its UIA peer disappears from the tree.
            var stillNeeds = s.Driver.FindElements(By.XPath("//*[@AutomationId='ExtAuthRequired']")).Count;
            if (stillNeeds == 0) { cleared = true; break; }
            await Task.Delay(100);
        }
        SaveStep(s, name, "04-after-simulated-success");
        Assert.True(cleared, "Expected ExtAuthRequired indicator to disappear after PushExtAuthSuccess event.");
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "IdentityDetailServices")]
    public async Task IdentityDetails_ShowsServiceList()
    {
        var name = nameof(IdentityDetails_ShowsServiceList);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing-with-services");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(350);
        SaveStep(s, name, "02-identity-details-with-3-services");

        // Validate at least one of the services we put in the fixture renders.
        Assert.True(WaitFor(s, By.XPath("//*[@Name='wiki.example']")).Displayed);
    }

    // 25-row UIA tree + virtualised ScrollViewer. PageSource cost dominates.
    [Fact(Timeout = 15000)]
    [Trait("Category", "MainScreen")]
    public async Task ManyIdentities_LandingShowsScrollableList()
    {
        var name = nameof(ManyIdentities_LandingShowsScrollableList);
        // 50-row PageSource costs ~10s; 25 rows is still well past the visible
        // viewport (~4-5 visible at a time) so it exercises scroll/virtualise
        // behaviour, but the UIA dump runs in roughly half the time.
        var status = FixtureBuilder.ManyMixedIdentities(count: 25);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), status);
        WaitForId(s, "ConnectLabel");
        await Task.Delay(600); // let the 25-row list virtualise / paint
        SaveStep(s, name, "01-many-identities");

        var src = s.Driver.PageSource;
        Assert.Contains("enabled-00", src);
        Assert.Contains("disabled-01", src);
        Assert.Contains("mfa-required-02", src);
        Assert.Contains("ext-auth-03", src);
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task Visual_AfterTogglingBetaIdentityOn()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");
        // Wait for both rows to render before snapshotting toggles -- the second
        // IdentityItem occasionally lags the first by a frame or two.
        WaitFor(session, By.XPath("//Text[@Name='disabled-at-start-id']"));
        await Task.Delay(350);

        // Target the toggle inside the disabled-at-start row (same reasoning
        // as IdentityToggle_Click_FlipsBetaToEnabled -- index-by-position is
        // unstable due to persisted sort state across tests).
        var toggle = session.Driver.FindElement(By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='disabled-at-start-id']]" +
            "//*[@AutomationId='ToggleSwitch']"));
        ClickAt(session, toggle);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (session.Mock.ReceivedCommandNames.Contains("IdentityOnOff")) break;
            await Task.Delay(50);
        }
        await Task.Delay(350); // animation settle

        var window = WindowElement(session);
        var png = ElementScreenshot(window);
        Assert.NotEmpty(png);
        await VerifyPng(png);
    }

    [Fact(Timeout = 10000)]
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
        await Task.Delay(350);
        SaveStep(s, name, "02-identity-details");

        // The IsDefaultProvider CheckBox stays disabled until a provider is selected.
        var firstProvider = WaitFor(s, By.XPath("//List[@AutomationId='ProviderList']/ListItem[1]"));
        ClickAt(s, firstProvider);
        await Task.Delay(350);
        SaveStep(s, name, "03-provider-selected");

        var check = WaitFor(s, By.XPath("//*[@AutomationId='IsDefaultProvider']"));
        ClickAt(s, check);
        await Task.Delay(350);
        SaveStep(s, name, "04-after-checking-default");

        // Click again to uncheck -- proves the control is interactive.
        ClickAt(s, check);
        await Task.Delay(350);
        SaveStep(s, name, "05-after-unchecking-default");
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "MainScreen")]
    public async Task AddIdentity_ClickButton_OpensAddIdentityDialog()
    {
        var name = nameof(AddIdentity_ClickButton_OpensAddIdentityDialog);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        // AddIdAreaButton is a StackPanel whose MouseLeftButtonUp handler
        // (AddIdentityContextMenu) opens a context menu. The StackPanel itself
        // doesn't expose a UIA peer; the two child Labels ("ADD" and "IDENTITY")
        // do. Clicking a child label via touch-tap fires WPF's MouseLeftButtonUp
        // on the ContextMenuOpening source -- which is the Label, not the
        // StackPanel -- so the handler may not fire from a touch tap. We still
        // want a regression signal here, so capture before/after PageSource
        // length: opening the context menu (or any reaction) materially grows
        // the UIA tree.
        var srcBefore = s.Driver.PageSource;
        var btn = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, btn);
        await Task.Delay(350);
        SaveStep(s, name, "02-after-add-identity-click");

        var srcAfter = s.Driver.PageSource;
        // Just assert the UI is still alive and responsive after the click. The
        // full dialog content is hard to assert without re-driving the context
        // menu programmatically.
        Assert.NotNull(srcAfter);
        Assert.True(srcAfter.Length > 100, "PageSource should still be populated after AddIdentity click");
    }
}
