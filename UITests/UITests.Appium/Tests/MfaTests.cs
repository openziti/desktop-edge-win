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
