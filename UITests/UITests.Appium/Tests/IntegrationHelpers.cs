using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>Steps shared by the integration tests.</summary>
public static class IntegrationHelpers
{
    // Steps that wait on a controller round trip, well past the mock's instant replies.
    public static readonly TimeSpan ControllerTimeout = TimeSpan.FromSeconds(30);

    public static int IdentityRowCount(AppiumSession s) =>
        s.Driver.FindElements(By.XPath("//Custom[@ClassName='IdentityItem']")).Count;

    public static By ToggleStatus(string identityName, string status) => By.XPath(
        $"//Custom[@ClassName='IdentityItem' and .//Text[@Name='{identityName}']]" +
        $"//*[@AutomationId='ToggleStatus' and @Name='{status}']");

    public static bool UiSent(RelayIpcServer relay, string command) =>
        relay.Recorded.Any(r => r.From == "ui" && r.Pipe == "cmd" && r.Line.Contains($"\"Command\":\"{command}\""));

    /// <summary>ZET's reply on the cmd pipe to the latest UI command line containing uiLineFragment.</summary>
    public static JObject ZetReplyTo(RelayIpcServer relay, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = relay.Recorded;
        int sent = -1;
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (recorded[i].From == "ui" && recorded[i].Pipe == "cmd" && recorded[i].Line.Contains(uiLineFragment))
            {
                sent = i;
                break;
            }
        }
        if (sent < 0)
            throw new InvalidOperationException($"the UI sent no cmd line containing {uiLineFragment}");
        for (int i = sent + 1; i < recorded.Count; i++)
        {
            if (recorded[i].From == "zet" && recorded[i].Pipe == "cmd")
                return JObject.Parse(recorded[i].Line);
        }
        throw new InvalidOperationException($"ZET sent no reply to: {recorded[sent].Line}");
    }

    public static void WaitForController(AppiumSession s, By by, string description) =>
        WaitUntil(s, description, ControllerTimeout, () => s.Driver.FindElements(by).Any(e => e.Displayed));

    /// <summary>
    /// Empty the fixture's ZET and launch the app against it, recording to captures\testName.
    /// </summary>
    public static async Task<AppiumSession> LaunchAsync(IntegrationFixture fixture, string testName)
    {
        await fixture.Zet.RemoveAllIdentitiesAsync();
        AppiumSession s = await AppiumSession.LaunchAgainstZetAsync(DefaultExePath(),
            IntegrationFixture.ZetDiscriminator,
            Path.Combine(RepoRoot(), "UITests", "TestResults", "captures", $"{testName}.jsonl"));
        WaitForId(s, "ConnectLabel");
        DismissWelcome(s);
        return s;
    }

    /// <summary>Add an identity from the imported fixture through Add Identity, With JWT.</summary>
    public static void AddIdentity(IntegrationFixture fixture, AppiumSession s, string identityName)
    {
        WriteTestJwt(fixture.Quickstart.GetJwtFromController(identityName));
        ClickAddIdentityWithJwt(s);
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
    }

    public record MfaEnrollment(string Secret, IReadOnlyList<string> RecoveryCodes);

    /// <summary>
    /// The UI's EnrollAndVerifyMFA: add the identity, enroll MFA from its details with a valid TOTP code, and return
    /// to the landing list.
    /// </summary>
    public static MfaEnrollment AddIdentityAndEnrollMfa(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        AddIdentity(fixture, s, identityName);
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']")));
        WaitForController(s, By.XPath("//*[@AutomationId='SetupCode']"), "the MFA setup dialog opens");
        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        WaitForId(s, "SetupCode").SendKeys(Totp.Compute(secret, DateTimeOffset.UtcNow));
        WaitForId(s, "AuthSetupButton").Click();
        WaitForController(s, By.XPath("//Text[@Name='MFA Recovery Codes']"), "the recovery codes show");
        // The codes are TextBoxes MFAScreen adds with no AutomationId, and a TextBox's text is its UIA Value.
        List<string> recoveryCodes = s.Driver
            .FindElements(By.XPath("//Text[@Name='MFA Recovery Codes']/following-sibling::Edit"))
            .Select(e => e.Text)
            .ToList();
        if (recoveryCodes.Count == 0)
            throw new InvalidOperationException($"no recovery codes were shown after enrolling {identityName}");
        ClickUntilGone(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        CloseIdentityDetails(s);
        return new MfaEnrollment(secret, recoveryCodes);
    }
}
