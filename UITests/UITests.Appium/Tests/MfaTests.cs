using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>MFA tests. Each launches its own session because MFA flows change identity state and open dialogs.</summary>
[TestLifecycleLog]
[Trait("Category", "Mfa")]
public class MfaTests
{
    // The toggle handler is on ToggleField inside the IdentityMFA MenuEditToggle. Clicking the outer control
    // doesn't reach it.
    private static readonly By MfaToggle =
        By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']");

    private static By RowMfaRequired(string identityName) => By.XPath(
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]//*[@AutomationId='MfaRequired']");

    [Fact(Timeout = 20000)]
    public async Task EnablingMfaShowsSetupDialog()
    {
        string name = nameof(EnablingMfaShowsSetupDialog);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"));
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        ClickAt(s, WaitFor(s, MfaToggle));

        // The enrollment_challenge event that follows EnableMFA opens the setup dialog.
        WaitForId(s, "SetupCode");
        SaveStep(s, name, "03-qr-dialog");

        JObject req = WaitForCommand(s, "EnableMFA", 0);
        Assert.Equal("c:\\fake\\ids\\enabled-id.json", (string?)req["Data"]?["Identifier"]);
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "Screenshots")]
    public async Task NeededMfaShowsOnRow()
    {
        string name = nameof(NeededMfaShowsOnRow);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("mfa-needed.json"));
        WaitForId(s, "ConnectLabel");

        WaitFor(s, RowMfaRequired("mfa-needed-id"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-landing-mfa-needed");
        await VerifyScreen(Capture(s), "mfa-needed-row");
    }

    [Fact(Timeout = 20000)]
    public async Task SatisfiedMfaShowsNoPrompt()
    {
        string name = nameof(SatisfiedMfaShowsNoPrompt);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("mfa-enabled.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-landing-mfa-enabled");

        Assert.Empty(s.Driver.FindElements(RowMfaRequired("mfa-enabled-id")));
    }

    [Fact(Timeout = 20000)]
    [Trait("Category", "Screenshots")]
    public async Task DisablingMfaAsksForCode()
    {
        string name = nameof(DisablingMfaAsksForCode);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("mfa-enabled.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        SaveStep(s, name, "01-landing-mfa-enabled");

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        ClickAt(s, WaitFor(s, MfaToggle));
        WaitForId(s, "AuthCode");
        await Trace.Settle(350);
        SaveStep(s, name, "03-after-toggle-off");
        await VerifyScreen(Capture(s), "mfa-code-prompt");

        // RemoveMFA only goes out once a code is submitted.
        Assert.DoesNotContain("RemoveMFA", s.Mock.ReceivedCommandNames);
    }

    [Fact(Timeout = 30000)]
    public async Task ValidCodeRemovesMfa()
    {
        string name = nameof(ValidCodeRemovesMfa);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("mfa-enabled.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));
        SaveStep(s, name, "01-landing-mfa-enabled");

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Trace.Settle(350);

        ClickAt(s, WaitFor(s, MfaToggle));
        IWebElement codeBox = WaitForId(s, "AuthCode");
        SaveStep(s, name, "02-mfa-code-prompt");

        codeBox.SendKeys(MockIpcServer.AcceptedMfaCode);
        await Trace.Settle(150);
        SaveStep(s, name, "03-code-typed");

        WaitForId(s, "AuthButton").Click();

        JObject req = WaitForCommand(s, "RemoveMFA", 0);
        WaitForGone(s, By.XPath("//*[@AutomationId='AuthCode']"));
        SaveStep(s, name, "04-after-authenticate");

        Assert.Equal(MockIpcServer.AcceptedMfaCode, (string?)req["Data"]?["Code"]);
        Assert.Empty(s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']")));
    }

    /// <summary>A rejected code keeps the prompt open and MFAScreen clears the code box.</summary>
    [Fact(Timeout = 30000)]
    public async Task InvalidCodeKeepsMfaPrompt()
    {
        string name = nameof(InvalidCodeKeepsMfaPrompt);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("mfa-enabled.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='mfa-enabled-id']"));

        OpenIdentityDetails(s, "mfa-enabled-id");
        await Trace.Settle(350);

        ClickAt(s, WaitFor(s, MfaToggle));
        IWebElement codeBox = WaitForId(s, "AuthCode");
        codeBox.SendKeys(MockIpcServer.RejectedMfaCode);
        await Trace.Settle(150);
        SaveStep(s, name, "01-rejected-code-typed");

        WaitForId(s, "AuthButton").Click();

        JObject req = WaitForCommand(s, "RemoveMFA", 0);
        await Trace.Settle(400);
        SaveStep(s, name, "02-after-rejection");

        Assert.Equal(MockIpcServer.RejectedMfaCode, (string?)req["Data"]?["Code"]);

        System.Collections.ObjectModel.ReadOnlyCollection<AppiumElement> codeBoxAfter = s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']"));
        Assert.True(codeBoxAfter.Count > 0, "Expected MFA prompt to still be open after rejected code.");
        Assert.Equal("", codeBoxAfter[0].Text);
    }
}
