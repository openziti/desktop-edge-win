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

    public const string AddIdentityLine = "\"Command\":\"AddIdentity\"";
    public const string EnableMfaLine = "\"Command\":\"EnableMFA\"";
    public const string VerifyMfaLine = "\"Command\":\"VerifyMFA\"";
    public const string SubmitMfaLine = "\"Command\":\"SubmitMFA\"";

    private static int LatestUiCmdLine(IReadOnlyList<RelayIpcServer.RecordedLine> recorded, string uiLineFragment)
    {
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (recorded[i].From == "ui" && recorded[i].Pipe == "cmd" && recorded[i].Line.Contains(uiLineFragment))
                return i;
        }
        throw new InvalidOperationException($"the UI sent no cmd line containing {uiLineFragment}");
    }

    /// <summary>ZET's reply on the cmd pipe to the latest UI command line containing uiLineFragment.</summary>
    public static JObject ZetReplyTo(RelayIpcServer relay, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = relay.Recorded;
        int sent = LatestUiCmdLine(recorded, uiLineFragment);
        for (int i = sent + 1; i < recorded.Count; i++)
        {
            if (recorded[i].From == "zet" && recorded[i].Pipe == "cmd")
                return JObject.Parse(recorded[i].Line);
        }
        throw new InvalidOperationException($"ZET sent no reply to: {recorded[sent].Line}");
    }

    /// <summary>
    /// Wait for ZET's first event line containing eventFragment after the latest UI command line containing
    /// uiLineFragment, as ZET's tests wait for an event after their command.
    /// </summary>
    public static JObject WaitForZetEventAfter(AppiumSession s, string uiLineFragment, string eventFragment)
    {
        JObject? found = null;
        WaitUntil(s, $"ZET sends an event containing {eventFragment} after {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            RelayIpcServer.RecordedLine? line = recorded.Skip(LatestUiCmdLine(recorded, uiLineFragment) + 1)
                .FirstOrDefault(r => r.From == "zet" && r.Pipe == "event" && r.Line.Contains(eventFragment));
            found = line == null ? null : JObject.Parse(line.Line);
            return found != null;
        });
        return found!;
    }

    /// <summary>ZET's MfaEvent.AssertSuccess on the first mfa event with action after the command.</summary>
    public static JObject AssertMfaEventSucceeded(AppiumSession s, string uiLineFragment, string action)
    {
        JObject mfa = WaitForZetEventAfter(s, uiLineFragment, $"\"Op\":\"mfa\",\"Action\":\"{action}\"");
        Assert.True((bool?)mfa["Successful"] == true, $"ZET's mfa {action} event failed: {mfa}");
        return mfa;
    }

    /// <summary>ZET's IdentityEvent.AssertMfaAuthenticated on the first identity updated event after the command.</summary>
    public static void AssertMfaAuthenticated(AppiumSession s, string uiLineFragment)
    {
        JObject updated = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"identity\",\"Action\":\"updated\"");
        Assert.True((bool?)updated["Id"]!["MfaEnabled"] == true && (bool?)updated["Id"]!["MfaNeeded"] == false,
            $"ZET's identity updated event is not MFA authenticated: {updated}");
    }

    public static string AddedIdentityFile(IntegrationFixture fixture) =>
        Path.Combine(fixture.Zet.IdentityDir, $"{AddedIdentityFileName}.json");

    /// <summary>ZET's AssertValidJwtEnrolledIdentityFile.</summary>
    public static void AssertJwtEnrolledIdentityFile(string path)
    {
        JObject file = JObject.Parse(File.ReadAllText(path));
        foreach (string field in new[] { "ztAPI", "id.cert", "id.key", "id.ca" })
            Assert.False(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has no {field}");
    }

    public static void WaitForController(AppiumSession s, By by, string description) =>
        WaitUntil(s, description, ControllerTimeout, () => s.Driver.FindElements(by).Any(e => e.Displayed));

    /// <summary>
    /// Wait until the blurb says text. An XPath wait can take long enough on a prompt screen that the capture after it
    /// misses the 2.5s blurb.
    /// </summary>
    public static void WaitForBlurb(AppiumSession s, string text) =>
        WaitUntil(s, $"the blurb says '{text}'", ControllerTimeout, () => BlurbShows(s, text));

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

    /// <summary>
    /// Add an identity from the imported fixture through Add Identity, With JWT, asserting what ZET's EnrollJwt does.
    /// Returns ZET's identity added event.
    /// </summary>
    public static JObject AddIdentity(IntegrationFixture fixture, AppiumSession s, string identityName)
    {
        WriteTestJwt(fixture.Quickstart.GetJwtFromController(identityName));
        ClickAddIdentityWithJwt(s);
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, AddIdentityLine)["Code"]);
        JObject added = WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"identity\",\"Action\":\"added\"");
        Assert.True((bool?)added["Id"]!["Active"] == true, $"ZET's identity added event is not active: {added}");
        AssertJwtEnrolledIdentityFile(AddedIdentityFile(fixture));
        SortByNameAscending(s);
        return added;
    }

    public record MfaEnrollment(string Secret, IReadOnlyList<string> RecoveryCodes);

    // The codes are TextBoxes MFAScreen adds with no AutomationId, and a TextBox's text is its UIA Value.
    public static readonly By RecoveryCodeBoxes =
        By.XPath("//Text[@Name='MFA Recovery Codes']/following-sibling::Edit");

    // Six capital letters and digits draw about 50 to 70px wide.
    public const int RecoveryCodeMaskWidth = 80;

    // The dialog's gradient runs from 249 to 247 grey over the codes, inside the comparer's colour tolerance of this.
    public static readonly System.Drawing.Brush RecoveryDialogBackground =
        new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(248, 248, 248));

    public static List<string> ReadRecoveryCodes(AppiumSession s) =>
        s.Driver.FindElements(RecoveryCodeBoxes).Select(e => e.Text).ToList();

    /// <summary>
    /// Add the identity and enroll MFA from its details with a valid TOTP code, leaving the recovery codes dialog
    /// open over the details.
    /// </summary>
    public static MfaEnrollment EnrollMfaToRecoveryCodes(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        JObject added = AddIdentity(fixture, s, identityName);
        Assert.True((bool?)added["Id"]!["MfaEnabled"] == false, $"MFA is enabled before EnableMFA: {added}");
        WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']")));
        WaitForController(s, By.XPath("//*[@AutomationId='SetupCode']"), "the MFA setup dialog opens");
        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        WaitForId(s, "SetupCode").SendKeys(Totp.Compute(secret, DateTimeOffset.UtcNow));
        WaitForId(s, "AuthSetupButton").Click();
        WaitForController(s, By.XPath("//Text[@Name='MFA Recovery Codes']"), "the recovery codes show");
        AssertMfaEnrollmentVerified(s);
        List<string> recoveryCodes = ReadRecoveryCodes(s);
        if (recoveryCodes.Count == 0)
            throw new InvalidOperationException($"no recovery codes were shown after enrolling {identityName}");
        return new MfaEnrollment(secret, recoveryCodes);
    }

    /// <summary>The asserts ZET's EnrollAndVerifyMFA makes from EnableMFA on, once the recovery codes show.</summary>
    public static void AssertMfaEnrollmentVerified(AppiumSession s)
    {
        JObject enable = ZetReplyTo(s.Relay!, EnableMfaLine);
        Assert.Equal(0, (int?)enable["Code"]);
        Assert.False(string.IsNullOrEmpty((string?)enable["Data"]!["ProvisioningUrl"]), $"EnableMFA reply has no ProvisioningUrl: {enable}");
        Assert.NotEmpty(enable["Data"]!["RecoveryCodes"]!.Values<string>());
        Assert.True((bool?)enable["Data"]!["IsVerified"] == false, $"EnableMFA reply is verified before VerifyMFA: {enable}");
        AssertMfaEventSucceeded(s, EnableMfaLine, "enrollment_challenge");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, VerifyMfaLine)["Code"]);
        AssertMfaAuthenticated(s, VerifyMfaLine);
        AssertMfaEventSucceeded(s, VerifyMfaLine, "enrollment_verification");
    }

    /// <summary>
    /// The UI's EnrollAndVerifyMFA: add the identity, enroll MFA from its details with a valid TOTP code, and return
    /// to the landing list.
    /// </summary>
    public static MfaEnrollment AddIdentityAndEnrollMfa(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(fixture, s, identityName);
        ClickUntilGone(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        CloseIdentityDetails(s);
        return enrollment;
    }

    /// <summary>ZET's triggerReauthChallenge from the row: off and back on, until the row asks to authenticate.</summary>
    public static void TriggerReauthChallenge(AppiumSession s, string identityName)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        // Shows on ZET's auth_challenge event.
        WaitForController(s, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
    }

    /// <summary>Submit code from the row's MFA prompt, saving the typed code as step.</summary>
    public static void SubmitFromRow(AppiumSession s, string name, string step, string identityName, string code)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "MfaRequired")));
        WaitForId(s, "AuthCode").SendKeys(code);
        SaveStep(s, name, step);
        WaitForId(s, "AuthButton").Click();
    }

    /// <summary>
    /// Submit code from the row's MFA prompt, wait for ZET to clear the lock, and assert what ZET's reauth tests do
    /// after a SubmitMFA that succeeds.
    /// </summary>
    public static async Task AuthenticateFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        SubmitFromRow(s, name, step, identityName, code);
        // Lets the SubmitMFA reply and ZET's mfa_auth_status event land before UIA polling loads the UI thread.
        await Task.Delay(1000);

        // Clears on ZET's mfa_auth_status event.
        WaitUntil(s, "the row stops asking to authenticate", ControllerTimeout,
            () => s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")).Count == 0);
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, SubmitMfaLine)["Code"]);
        AssertMfaAuthenticated(s, SubmitMfaLine);
        AssertMfaEventSucceeded(s, SubmitMfaLine, "mfa_auth_status");
    }

    /// <summary>
    /// Submit code from the row's MFA prompt, assert ZET rejects it and the prompt stays open, and return the
    /// capture taken while the failure blurb shows.
    /// </summary>
    public static async Task<byte[]> RejectFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        SubmitFromRow(s, name, step, identityName, code);
        // MFAScreen keeps the prompt open on a failed SubmitMFA reply.
        WaitForBlurb(s, "Authentication Failed");
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so this capture comes before the slower asserts.
        byte[] png = Capture(s);
        JObject reply = ZetReplyTo(s.Relay!, SubmitMfaLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.NotEmpty(s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']")));
        Assert.NotEmpty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        return png;
    }
}
