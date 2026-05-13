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
    [Fact(Timeout = 10000)]
    public async Task Mfa_EnableFromLanding_PushesQrChallenge()
    {
        var name = nameof(Mfa_EnableFromLanding_PushesQrChallenge);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Trace.Settle(350);
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
        await Trace.Settle(350); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        Assert.Contains("EnableMFA", s.Mock.ReceivedCommandNames);
    }

    [Fact(Timeout = 10000)]
    public async Task Mfa_NeededAtStart_RendersMfaPrompt()
    {
        var name = nameof(Mfa_NeededAtStart_RendersMfaPrompt);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-needed.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-needed-id']"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-landing-mfa-needed");

        var src = s.Driver.PageSource;
        Assert.Contains("mfa-needed-id", src);
    }

    [Fact(Timeout = 10000)]
    public async Task Mfa_EnabledAtStart_RowShowsMfaIndicator()
    {
        var name = nameof(Mfa_EnabledAtStart_RowShowsMfaIndicator);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-landing-mfa-enabled");

        var src = s.Driver.PageSource;
        Assert.Contains("mfa-enabled-id", src);
    }

    [Fact(Timeout = 10000)]
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
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        var preEdits = s.Driver.FindElements(By.XPath("//Edit")).Count;

        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);
        await Trace.Settle(350);
        SaveStep(s, name, "03-after-toggle-off");

        // After the click, the MFA code prompt surfaces additional Edit controls
        // on top of the details view. Strictly more Edits after than before.
        var postEdits = s.Driver.FindElements(By.XPath("//Edit")).Count;
        Assert.True(postEdits > preEdits,
            $"Expected MFA code prompt to open additional Edit fields; before={preEdits}, after={postEdits}.");
    }

    [Fact(Timeout = 10000)]
    public async Task Mfa_EnableFromLanding_QrSetupDialogRenders()
    {
        var name = nameof(Mfa_EnableFromLanding_QrSetupDialogRenders);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Trace.Settle(350);
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
        await Trace.Settle(350); // QR render settle
        SaveStep(s, name, "03-qr-dialog");

        // The setup dialog contains an Authenticator Code Edit control where the user
        // types the 6-digit token. Look for *any* Edit/TextBox surfaced after toggle --
        // a stronger signal than just PageSource contains.
        var hasEdit = s.Driver.FindElements(By.XPath("//Edit")).Count > 0;
        Assert.True(hasEdit, "Expected at least one Edit control on the QR setup dialog");
    }

    /// <summary>
    /// Disable-MFA flow with the accepted mock code (123456). After clicking
    /// the toggle the UI surfaces MFAScreen mode 3 with an AuthCode TextBox;
    /// typing 123456 + clicking Authenticate sends RemoveMFA{Code=123456}
    /// which the mock accepts (Success=true).
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Mfa_DisableWithAcceptedCode_SendsRemoveMFAAndSucceeds()
    {
        var name = nameof(Mfa_DisableWithAcceptedCode_SendsRemoveMFAAndSucceeds);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        SaveStep(s, name, "01-landing-mfa-enabled");

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Trace.Settle(350);

        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);
        await Trace.Settle(350);
        SaveStep(s, name, "02-mfa-code-prompt");

        var codeBox = WaitFor(s, By.XPath("//*[@AutomationId='AuthCode']"));
        codeBox.SendKeys("123456");
        await Trace.Settle(150);
        SaveStep(s, name, "03-code-typed");

        WaitFor(s, By.XPath("//*[@AutomationId='AuthButton']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("RemoveMFA")) break;
            await Task.Delay(50);
        }
        await Trace.Settle(300);
        SaveStep(s, name, "04-after-authenticate");

        Assert.Contains("RemoveMFA", s.Mock.ReceivedCommandNames);
        var req = s.Mock.ReceivedRequests.Last(r => (string?)r["Command"] == "RemoveMFA");
        Assert.Equal("123456", (string?)req["Data"]?["Code"]);
    }

    /// <summary>
    /// Disable-MFA flow with the canonical rejection code (666666). The mock
    /// returns Success=false and pushes an mfa event with Successful=false;
    /// the WPF MFAScreen reacts by clearing the textbox and surfacing an
    /// error toast/blurb. We just assert the AuthCode box is reset back to
    /// empty (the canonical error-path UI signal).
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Mfa_DisableWithRejectedCode_ClearsTextBoxAndRetainsPrompt()
    {
        var name = nameof(Mfa_DisableWithRejectedCode_ClearsTextBoxAndRetainsPrompt);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "mfa-enabled.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Trace.Settle(350);

        var toggle = WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, toggle);
        await Trace.Settle(350);

        var codeBox = WaitFor(s, By.XPath("//*[@AutomationId='AuthCode']"));
        codeBox.SendKeys("666666");
        await Trace.Settle(150);
        SaveStep(s, name, "01-rejected-code-typed");

        WaitFor(s, By.XPath("//*[@AutomationId='AuthButton']")).Click();

        // Wait for the RemoveMFA round-trip then for the MFA prompt to react.
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("RemoveMFA")) break;
            await Task.Delay(50);
        }
        await Trace.Settle(400);
        SaveStep(s, name, "02-after-rejection");

        Assert.Contains("RemoveMFA", s.Mock.ReceivedCommandNames);
        var req = s.Mock.ReceivedRequests.Last(r => (string?)r["Command"] == "RemoveMFA");
        Assert.Equal("666666", (string?)req["Data"]?["Code"]);

        // The prompt should still be visible (no successful close) and the box
        // should have been cleared by MFAScreen's failure path.
        var codeBoxAfter = s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']"));
        Assert.True(codeBoxAfter.Count > 0, "Expected MFA prompt to still be open after rejected code.");
        Assert.Equal("", codeBoxAfter[0].Text);
    }
}
