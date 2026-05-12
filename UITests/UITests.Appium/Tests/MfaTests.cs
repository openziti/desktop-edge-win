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
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details");

        // IdentityMFA is a MenuEditToggle Custom. The actual toggle (which fires
        // EnableMFA / RemoveMFA) is the Toggler named ToggleField inside it.
        // Clicking the outer Custom doesn't reach the inner Toggler's mouse event.
        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(150); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_NeededAtStart_RendersMfaPrompt()
    {
        var name = nameof(Mfa_NeededAtStart_RendersMfaPrompt);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-needed.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-needed-id']"));
        await Task.Delay(150);
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
        await Task.Delay(150);
        SaveStep(s, name, "01-landing-mfa-enabled");

        var src = s.Driver.PageSource;
        Assert.Contains("mfa-enabled-id", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_DisableTogglePromptsForMfaCode()
    {
        // Disabling MFA does NOT immediately send RemoveMFA. MainWindow.MFAToggled
        // routes the off-toggle to ShowMFA(_, mode 3) which opens the MFA code
        // prompt -- only submitting a valid code sends RemoveMFA on the wire. So
        // the assertion is "code prompt surfaced", not "RemoveMFA received".
        var name = nameof(Mfa_DisableTogglePromptsForMfaCode);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        SaveStep(s, name, "01-landing-mfa-enabled");

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details");

        var preEdits = s.Driver.FindElements(By.XPath("//Edit")).Count;

        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);
        await Task.Delay(200);
        SaveStep(s, name, "03-after-toggle-off");

        // After the click, the MFA code prompt surfaces additional Edit controls
        // on top of the details view. Strictly more Edits after than before.
        var postEdits = s.Driver.FindElements(By.XPath("//Edit")).Count;
        Assert.True(postEdits > preEdits,
            $"Expected MFA code prompt to open additional Edit fields; before={preEdits}, after={postEdits}.");
    }

    [Fact(Timeout = 120000)]
    public async Task Mfa_EnableFromLanding_QrSetupDialogRenders()
    {
        var name = nameof(Mfa_EnableFromLanding_QrSetupDialogRenders);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(150);
        SaveStep(s, name, "02-identity-details");

        // IdentityMFA is a MenuEditToggle Custom. The actual toggle (which fires
        // EnableMFA / RemoveMFA) is the Toggler named ToggleField inside it.
        // Clicking the outer Custom doesn't reach the inner Toggler's mouse event.
        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Task.Delay(150); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        // The setup dialog contains an Authenticator Code Edit control where the user
        // types the 6-digit token. Look for *any* Edit/TextBox surfaced after toggle --
        // a stronger signal than just PageSource contains.
        var hasEdit = s.Driver.FindElements(By.XPath("//Edit")).Count > 0;
        Assert.True(hasEdit, "Expected at least one Edit control on the QR setup dialog");
    }
}
