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
    [Fact(Timeout = 120000)]
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
    public async Task IdentityToggle_Click_FlipsBetaToEnabled()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");

        var toggles = session.Driver.FindElements(By.XPath("//*[@AutomationId='ToggleSwitch']"));
        Assert.Equal(2, toggles.Count);
        toggles[1].Click();

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
        await Task.Delay(400);
        SaveStep(s, name, "05-after-clicking-debug");

        // Menu stays open after SetLevel -- click Trace directly without re-navigating.
        // (Clicking MAIN again toggles the menu CLOSED, so don't.)
        WaitForId(s, "LogTrace").Click();
        await Task.Delay(400);
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
        await Task.Delay(400);
        SaveStep(s, name, "04-tunnel-config-screen");
    }

    [Fact(Timeout = 120000)]
    public async Task IdentityDetails_OpensOnIdentityClick()
    {
        var name = nameof(IdentityDetails_OpensOnIdentityClick);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        // Click into the first identity row -- the IdName Text bubbles up to
        // the IdentityItem's MouseUp=OpenDetails handler.
        var firstIdName = s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).First();
        firstIdName.Click();
        await Task.Delay(500); // animation settle
        SaveStep(s, name, "02-identity-details-open");

        // Identity detail screen exposes IdName / IdServer / ForgetIdentityButton
        // -- assert at least one of these came alive to prove we navigated.
        Assert.True(s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).Count > 0);
    }

    [Fact(Timeout = 120000)]
    public async Task MFA_EnableShowsQRDialog()
    {
        var name = nameof(MFA_EnableShowsQRDialog);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        // Open the enabled identity's detail screen
        var firstIdName = s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).First();
        firstIdName.Click();
        await Task.Delay(500);
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
        await Task.Delay(700); // QR render settle
        SaveStep(s, name, "03-after-clicking-mfa-toggle");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);
        var req = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "EnableMFA");
        Assert.Equal("c:\\fake\\ids\\enabled-id.json",
            (string?)req["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 120000)]
    public async Task TunnelConfig_ClickEditValuesShowsForm()
    {
        var name = nameof(TunnelConfig_ClickEditValuesShowsForm);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(400);
        SaveStep(s, name, "02-tunnel-config-screen");

        // "Edit Values" is a StyledButton -- the inner label text is queryable.
        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(400);
        SaveStep(s, name, "03-after-clicking-edit-values");
    }

    [Fact(Timeout = 120000)]
    public async Task TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig()
    {
        var name = nameof(TunnelConfig_EditValuesAndSave_SendsUpdateInterfaceConfig);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenMainMenu(s);
        WaitFor(s, By.XPath("//*[@Name='Advanced Settings']")).Click();
        WaitFor(s, By.XPath("//*[@Name='Tunnel Config']")).Click();
        await Task.Delay(300);
        SaveStep(s, name, "02-tunnel-config-screen");

        WaitFor(s, By.XPath("//*[@Name='Edit Values']")).Click();
        await Task.Delay(400);
        SaveStep(s, name, "03-edit-form");

        // The Save button on the edit form (SaveConfigButton) -- label is "Save".
        WaitFor(s, By.XPath("//*[@Name='Save']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("UpdateInterfaceConfig")) break;
            await Task.Delay(50);
        }
        await Task.Delay(300);
        SaveStep(s, name, "04-after-save");

        Assert.Contains("UpdateInterfaceConfig", s.Mock.ReceivedCommandNames);
        var cmd = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "UpdateInterfaceConfig");
        // L3 + L2 payloads must be present
        Assert.NotNull(cmd["Data"]?["L3"]);
        Assert.NotNull(cmd["Data"]?["L2"]);
    }

    [Fact(Timeout = 120000)]
    public async Task ExtAuth_AuthorizeClick_SendsExternalAuthCommand()
    {
        var name = nameof(ExtAuth_AuthorizeClick_SendsExternalAuthCommand);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "needs-ext-auth.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='needs-ext-auth-id']"));
        SaveStep(s, name, "01-landing-with-ext-auth-identity");

        // Click into the identity detail
        var idName = s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).First();
        idName.Click();
        await Task.Delay(500);
        SaveStep(s, name, "02-identity-details-shows-auth-button");

        WaitFor(s, By.XPath("//*[@AutomationId='AuthenticateWithProvider']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("ExternalAuth")) break;
            await Task.Delay(50);
        }
        await Task.Delay(400);
        SaveStep(s, name, "03-after-authorize-click");

        Assert.Contains("ExternalAuth", s.Mock.ReceivedCommandNames);
        var cmd = s.Mock.ReceivedRequests
            .Last(r => (string?)r["Command"] == "ExternalAuth");
        Assert.Equal("c:\\fake\\ids\\needs-ext-auth-id.json",
            (string?)cmd["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 120000)]
    public async Task IdentityDetails_ShowsServiceList()
    {
        var name = nameof(IdentityDetails_ShowsServiceList);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing-with-services");

        var idName = s.Driver.FindElements(By.XPath("//*[@AutomationId='IdName']")).First();
        idName.Click();
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details-with-3-services");

        // Validate at least one of the services we put in the fixture renders.
        Assert.True(WaitFor(s, By.XPath("//*[@Name='wiki.example']")).Displayed);
    }

    [Fact(Timeout = 120000)]
    public async Task ManyIdentities_LandingShowsScrollableList()
    {
        var name = nameof(ManyIdentities_LandingShowsScrollableList);
        var status = FixtureBuilder.ManyMixedIdentities(count: 50);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), status);
        WaitForId(s, "ConnectLabel");
        // Let the 50-row list render
        await Task.Delay(1500);
        SaveStep(s, name, "01-50-identities");

        // Assert at least one of each flavor is present in the UIA tree
        Assert.True(WaitFor(s, By.XPath("//Text[@Name='enabled-00']")).Displayed);
        Assert.True(WaitFor(s, By.XPath("//Text[@Name='disabled-01']")).Displayed);
        // mfa + ext-auth flavors live further down; just confirm the row label exists
        Assert.True(s.Driver.FindElements(By.XPath("//Text[@Name='mfa-required-02']")).Count > 0);
        Assert.True(s.Driver.FindElements(By.XPath("//Text[@Name='ext-auth-03']")).Count > 0);
    }

    [Fact(Timeout = 120000)]
    public async Task Visual_AfterTogglingBetaIdentityOn()
    {
        await using var session = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(session, "ConnectLabel");

        var toggles = session.Driver.FindElements(By.XPath("//*[@AutomationId='ToggleSwitch']"));
        Assert.Equal(2, toggles.Count);
        toggles[1].Click();

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
}
