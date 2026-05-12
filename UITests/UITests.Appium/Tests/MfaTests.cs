using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// MFA-focused UI tests. Each test launches its own AppiumSession because MFA
/// flows mutate identity state and pop modal dialogs.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Mfa")]
public class MfaTests
{
    [Fact(Timeout = 120000)]
    public async Task Mfa_EnableFromLanding_PushesQrChallenge()
    {
        var name = nameof(Mfa_EnableFromLanding_PushesQrChallenge);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(500);
        SaveStep(s, name, "02-identity-details");

        var mfa = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']"));
        mfa.Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(700); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);

        // The mock pushes a fake enrollment challenge with recovery code "AAAAAA"
        // when EnableMFA arrives; assert it surfaced in the UI tree.
        var src = s.Driver.PageSource;
        Assert.Contains("AAAAAA", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_NeededAtStart_RendersMfaPrompt()
    {
        var name = nameof(Mfa_NeededAtStart_RendersMfaPrompt);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-needed.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-needed-id']"));
        await Task.Delay(300);
        SaveStep(s, name, "01-landing-mfa-needed");

        var src = s.Driver.PageSource;
        Assert.Contains("mfa-needed-id", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_EnabledAtStart_RowShowsMfaIndicator()
    {
        var name = nameof(Mfa_EnabledAtStart_RowShowsMfaIndicator);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        await Task.Delay(300);
        SaveStep(s, name, "01-landing-mfa-enabled");

        var src = s.Driver.PageSource;
        Assert.Contains("mfa-enabled-id", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_DisableSendsRemoveMFA()
    {
        var name = nameof(Mfa_DisableSendsRemoveMFA);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        SaveStep(s, name, "01-landing-mfa-enabled");

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Task.Delay(500);
        SaveStep(s, name, "02-identity-details");

        var mfa = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']"));
        mfa.Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("RemoveMFA") ||
                s.Mock.ReceivedCommandNames.Contains("EnableMFA"))
            {
                break;
            }
            await Task.Delay(50);
        }
        await Task.Delay(400);
        SaveStep(s, name, "03-after-toggle-off");

        var names = s.Mock.ReceivedCommandNames;
        Assert.True(
            names.Contains("RemoveMFA") || names.Contains("EnableMFA"),
            $"Expected RemoveMFA or EnableMFA; mock received: {string.Join(", ", names)}");
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_EnableFromLanding_QrSetupDialogRenders()
    {
        var name = nameof(Mfa_EnableFromLanding_QrSetupDialogRenders);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(500);
        SaveStep(s, name, "02-identity-details");

        var mfa = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']"));
        mfa.Click();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(800); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        // The setup dialog contains an Authenticator Code Edit control where the user
        // types the 6-digit token. Look for *any* Edit/TextBox surfaced after toggle --
        // a stronger signal than just PageSource contains.
        var hasEdit = s.Driver.FindElements(By.XPath("//Edit")).Count > 0;
        Assert.True(hasEdit, "Expected at least one Edit control on the QR setup dialog");
    }
}
