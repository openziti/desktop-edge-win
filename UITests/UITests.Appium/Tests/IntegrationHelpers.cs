using Newtonsoft.Json;
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

    private static int UiCmdLineCount(RelayIpcServer relay, string uiLineFragment) =>
        relay.Recorded.Count(r => r.From == "ui" && r.Pipe == "cmd" && r.Line.Contains(uiLineFragment));

    public const string AddIdentityLine = "\"Command\":\"AddIdentity\"";
    public const string EnableMfaLine = "\"Command\":\"EnableMFA\"";
    public const string VerifyMfaLine = "\"Command\":\"VerifyMFA\"";
    public const string SubmitMfaLine = "\"Command\":\"SubmitMFA\"";
    // DataClient.ExternalAuthLogin sends this command.
    public const string ExternalAuthLine = "\"Command\":\"ExternalAuth\"";

    private static int LatestUiCmdLine(IReadOnlyList<RelayIpcServer.RecordedLine> recorded, string uiLineFragment)
    {
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (recorded[i].From == "ui" && recorded[i].Pipe == "cmd" && recorded[i].Line.Contains(uiLineFragment))
                return i;
        }
        throw new InvalidOperationException($"the UI sent no cmd line containing {uiLineFragment}");
    }

    /// <summary>The latest UI command line containing uiLineFragment.</summary>
    public static JObject UiCommand(RelayIpcServer relay, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = relay.Recorded;
        return JObject.Parse(recorded[LatestUiCmdLine(recorded, uiLineFragment)].Line);
    }

    private static JObject? FindZetReplyTo(IReadOnlyList<RelayIpcServer.RecordedLine> recorded, int sent)
    {
        for (int i = sent + 1; i < recorded.Count; i++)
        {
            if (recorded[i].From == "zet" && recorded[i].Pipe == "cmd")
                return JObject.Parse(recorded[i].Line);
        }
        return null;
    }

    /// <summary>ZET's reply on the cmd pipe to the latest UI command line containing uiLineFragment.</summary>
    public static JObject ZetReplyTo(RelayIpcServer relay, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = relay.Recorded;
        int sent = LatestUiCmdLine(recorded, uiLineFragment);
        return FindZetReplyTo(recorded, sent)
            ?? throw new InvalidOperationException($"ZET sent no reply to: {recorded[sent].Line}");
    }

    /// <summary>Wait for ZET's reply to the latest UI command line containing uiLineFragment, which must be sent.</summary>
    public static JObject WaitForZetReplyTo(AppiumSession s, string uiLineFragment)
    {
        JObject? reply = null;
        WaitUntil(s, $"ZET replies to {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            reply = FindZetReplyTo(recorded, LatestUiCmdLine(recorded, uiLineFragment));
            return reply != null;
        });
        return reply!;
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

    /// <summary>
    /// ZET's assertExpectedIdentityName: the first identity added event names the identity file, and a later one carries
    /// the name the signer's name claims selector resolved to.
    /// </summary>
    public static void WaitForIdentityNamed(AppiumSession s, string uiLineFragment, string expectedName)
    {
        const string addedFragment = "\"Op\":\"identity\",\"Action\":\"added\"";
        WaitUntil(s, $"ZET sends identity added named {expectedName} after {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            return recorded.Skip(LatestUiCmdLine(recorded, uiLineFragment) + 1)
                .Where(r => r.From == "zet" && r.Pipe == "event" && r.Line.Contains(addedFragment))
                .Any(r => (string?)JObject.Parse(r.Line)["Id"]?["Name"] == expectedName);
        });
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

    private static readonly TimeSpan IdentityFileReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The identity file, read again while ZET holds it open: ZET rewrites it after enrollment (the HA controller list),
    /// and a read in between fails with a sharing violation or lands on a half-written file.
    /// </summary>
    private static JObject ReadIdentityFile(string path)
    {
        DateTime deadline = DateTime.UtcNow + IdentityFileReadTimeout;
        while (true)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception e) when ((e is IOException || e is JsonReaderException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>ZET's AssertValidJwtEnrolledIdentityFile.</summary>
    public static void AssertJwtEnrolledIdentityFile(string path)
    {
        JObject file = ReadIdentityFile(path);
        foreach (string field in new[] { "ztAPI", "id.cert", "id.key", "id.ca" })
            Assert.False(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has no {field}");
    }

    /// <summary>ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-none: a CA bundle and no cert or key.</summary>
    public static void AssertUrlEnrolledToNoneIdentityFile(string path)
    {
        JObject file = ReadIdentityFile(path);
        foreach (string field in new[] { "ztAPI", "id.ca" })
            Assert.False(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has no {field}");
        foreach (string field in new[] { "id.cert", "id.key" })
            Assert.True(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has {field} after enroll-to-none");
    }

    /// <summary>Open Add Identity, With URL, and type url over whatever the dialog prefilled from the clipboard.</summary>
    public static void EnterControllerUrl(AppiumSession s, string url)
    {
        ClickAddIdentityWithUrl(s);
        IWebElement box = WaitForId(s, "ControllerURL");
        // IWebElement.Clear() doesn't reliably fire WPF's TextChanged, which resets the dialog's signer state.
        box.SendKeys(Keys.Control + "a" + Keys.Control);
        box.SendKeys(Keys.Delete);
        box.SendKeys(url);
    }

    /// <summary>
    /// Add an identity through Add Identity, With URL, against a controller with no ext-jwt signers, asserting what
    /// ZET's EnrollUrlIdentityToNone does. Returns ZET's identity needs_ext_login event.
    /// </summary>
    public static JObject AddIdentityByUrl(AppiumSession s, string url)
    {
        EnterControllerUrl(s, url);
        return JoinEnrolledToNone(s);
    }

    // AddIdentitySignerChoice's title. The dialog is collapsed, so out of the UIA tree, until the app opens it.
    public static readonly By EnrollChoiceTitle = By.XPath("//*[@Name='Configure Enrollment']");

    /// <summary>
    /// Click Join Network on the URL dialog, then wait until the app either sends AddIdentity or opens the enrollment
    /// choice dialog instead. Returns whether the choice dialog opened.
    /// </summary>
    public static bool JoinOpensEnrollChoice(AppiumSession s)
    {
        int addsBefore = UiCmdLineCount(s.Relay!, AddIdentityLine);
        WaitForId(s, "JoinNetworkBtn").Click();
        // The app looks up the controller's ext-jwt signers before it does either.
        WaitUntil(s, "the UI sends AddIdentity or opens the enrollment choice", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, AddIdentityLine) > addsBefore
                || s.Driver.FindElements(EnrollChoiceTitle).Count > 0);
        return s.Driver.FindElements(EnrollChoiceTitle).Count > 0;
    }

    /// <summary>Click Join Network on the URL dialog and assert the app sends AddIdentity with no enrollment choice.</summary>
    public static void JoinWithoutEnrollChoice(AppiumSession s) =>
        Assert.False(JoinOpensEnrollChoice(s), "the app opened the enrollment choice instead of sending AddIdentity");

    // A RadioButton's UIA Name is empty when its content is a panel, so each is found by its label's TextBlock.
    public static readonly By UserSessionRadio = By.XPath("//RadioButton[.//*[@Name='User session']]");
    public static readonly By DeviceCertificateRadio = By.XPath("//RadioButton[.//*[@Name='Device certificate']]");
    // Shown only when more than one signer can enroll.
    public static readonly By SignerPickerLabel = By.XPath("//*[@Name='Identity Provider']");

    /// <summary>Enter the controller URL and click Join Network, asserting the enrollment choice dialog opens.</summary>
    public static async Task OpenEnrollChoice(AppiumSession s)
    {
        await PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        Assert.True(JoinOpensEnrollChoice(s), "the app sent AddIdentity without offering the enrollment choice");
    }

    public static void ChooseDeviceCertificate(AppiumSession s)
    {
        IWebElement deviceCertificate = WaitFor(s, DeviceCertificateRadio);
        ClickAt(s, deviceCertificate);
        WaitUntil(s, "Device certificate is selected", TimeSpan.FromSeconds(5), () => deviceCertificate.Selected);
        Assert.False(WaitFor(s, UserSessionRadio).Selected, "User session stayed selected");
    }

    /// <summary>Assert the AddIdentity the app sent carries this enroll mode and no provider, as it does with one capable signer.</summary>
    public static void AssertSentEnrollMode(AppiumSession s, string expected)
    {
        JObject sent = UiCommand(s.Relay!, AddIdentityLine);
        Assert.Equal(expected, (string?)sent["Data"]!["EnrollMode"]);
        Assert.Null((string?)sent["Data"]!["Provider"]);
    }

    /// <summary>
    /// Click Join Network on the open enrollment choice dialog. Returns the IdP URL from ZET's AddIdentity reply.
    /// </summary>
    public static string JoinFromEnrollChoice(AppiumSession s)
    {
        // Both dialogs have a JoinNetworkBtn, and the URL dialog's stays in the tree until its fade out collapses it.
        WaitForGone(s, By.XPath("//*[@AutomationId='ControllerURL']"));
        int addsBefore = UiCmdLineCount(s.Relay!, AddIdentityLine);
        WaitForId(s, "JoinNetworkBtn").Click();
        WaitUntil(s, "the UI sends AddIdentity", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, AddIdentityLine) > addsBefore);
        return EnrollmentUrlFromReply(s);
    }

    /// <summary>Click Join Network on the URL dialog and assert what ZET's EnrollUrlIdentityToNone does.</summary>
    public static JObject JoinEnrolledToNone(AppiumSession s)
    {
        JoinWithoutEnrollChoice(s);
        JObject needsLogin = WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"identity\",\"Action\":\"needs_ext_login\"");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, AddIdentityLine)["Code"]);
        string? identifier = (string?)needsLogin["Id"]!["Identifier"];
        Assert.False(string.IsNullOrEmpty(identifier), $"needs_ext_login has no Identifier: {needsLogin}");
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        AssertUrlEnrolledToNoneIdentityFile(identifier!);
        WaitUntil(s, "the URL identity shows on the landing list", ControllerTimeout, () => IdentityRowCount(s) == 1);
        return needsLogin;
    }

    /// <summary>Runs <paramref name="body"/> with the working signer set to <paramref name="enrollment"/>.</summary>
    public static async Task WithWorkingSigner(IntegrationFixture fixture, Quickstart.SignerEnrollment enrollment,
        Func<Task> body)
    {
        fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, enrollment);
        try
        {
            await body();
        }
        finally
        {
            CloseBrowsers();
            fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, Quickstart.EnrollToNone);
        }
    }

    /// <summary>ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-cert, which checks what the JWT one does.</summary>
    public static void AssertUrlEnrolledToCertIdentityFile(string path) => AssertJwtEnrolledIdentityFile(path);

    /// <summary>ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-token, which checks what the none one does.</summary>
    public static void AssertUrlEnrolledToTokenIdentityFile(string path) => AssertUrlEnrolledToNoneIdentityFile(path);

    // One row at a time, and its name is the controller's, which comes from a dex claim.
    public static readonly By ExtAuthRequiredIcon = By.XPath("//*[@AutomationId='ExtAuthRequired']");

    /// <summary>
    /// ZET's GetExternalAuthURL from the row: click its ext auth icon, which with one provider logs in to it directly.
    /// Returns the IdP URL from ZET's reply, which the app opens in the browser.
    /// </summary>
    public static string LoginFromRow(AppiumSession s)
    {
        // The browser opened for an earlier IdP URL can cover the icon, and the click is a real mouse click.
        CloseBrowsers();
        int loginsBefore = UiCmdLineCount(s.Relay!, ExternalAuthLine);
        ClickAt(s, WaitFor(s, ExtAuthRequiredIcon));
        WaitUntil(s, "the UI sends ExternalAuth", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, ExternalAuthLine) > loginsBefore);
        JObject reply = WaitForZetReplyTo(s, ExternalAuthLine);
        Assert.Equal(0, (int?)reply["Code"]);
        string? url = (string?)reply["Data"]?["url"];
        Assert.False(string.IsNullOrEmpty(url), $"ExternalAuth reply has no Data.url: {reply}");
        return url!;
    }

    /// <summary>ZET's wait for needs_ext_login after the command, until the row shows its ext auth icon.</summary>
    public static JObject WaitForNeedsExtLogin(AppiumSession s, string uiLineFragment)
    {
        JObject needsLogin = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"identity\",\"Action\":\"needs_ext_login\"");
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        WaitForController(s, ExtAuthRequiredIcon, "the row asks for external auth");
        return needsLogin;
    }

    /// <summary>
    /// Click Join Network on the URL dialog and assert what ZET's beginEnrollment does. Returns the IdP URL from ZET's
    /// reply, which the app opens in the browser.
    /// </summary>
    public static string JoinToEnrollmentUrl(AppiumSession s)
    {
        JoinWithoutEnrollChoice(s);
        return EnrollmentUrlFromReply(s);
    }

    /// <summary>
    /// Deny the IdP login ZET is waiting on and wait for ZET to fail the AddIdentity. An abandoned login holds ZET's
    /// loopback callback for 60s, and the next enrollment's code then lands on it and fails with "Invalid code_verifier".
    /// </summary>
    public static async Task DenyEnrollment(AppiumSession s, string authUrl)
    {
        await Dex.DenyIdPFlowAsync(authUrl);
        // ZET answers the AddIdentity a second time when the login ends.
        WaitUntil(s, "ZET fails the denied AddIdentity", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            return recorded.Skip(LatestUiCmdLine(recorded, AddIdentityLine) + 1)
                .Count(r => r.From == "zet" && r.Pipe == "cmd" && r.Line.Contains("\"Success\":false")) == 1;
        });
    }

    private static string EnrollmentUrlFromReply(AppiumSession s)
    {
        JObject reply = WaitForZetReplyTo(s, AddIdentityLine);
        Assert.Equal(0, (int?)reply["Code"]);
        string? url = (string?)reply["Data"]?["url"];
        Assert.False(string.IsNullOrEmpty(url), $"AddIdentity reply has no Data.url: {reply}");
        return url!;
    }

    /// <summary>
    /// ZET's assertGrantedServices: the fixture gates services on the attributes the signer's selector applies.
    /// </summary>
    public static void AssertGrantedServices(AppiumSession s, string uiLineFragment, IReadOnlyList<string> expected)
    {
        JObject bulk = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"bulkservice\",\"Action\":\"updated\"");
        List<string> granted = bulk["AddedServices"]!.Select(svc => (string)svc["Name"]!).ToList();
        Assert.Equal(expected.OrderBy(n => n), granted.OrderBy(n => n));
    }

    /// <summary>ZET's assertEnrollmentSucceeded up to the file check. Returns ZET's identity added event.</summary>
    public static JObject AssertEnrollmentAdded(AppiumSession s, string uiLineFragment)
    {
        JObject added = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"identity\",\"Action\":\"added\"");
        Assert.True((bool?)added["Id"]!["Active"] == true, $"ZET's identity added event is not active: {added}");
        Assert.True((bool?)added["Id"]!["NeedsExtAuth"] == false, $"ZET's identity added event still needs ext auth: {added}");
        return added;
    }

    /// <summary>
    /// Close the browser windows the app opened for an IdP URL. The test drives dex itself, so they only cover the app.
    /// </summary>
    public static void CloseBrowsers()
    {
        foreach (System.Diagnostics.Process browser in System.Diagnostics.Process.GetProcessesByName("msedge"))
        {
            using (browser)
            {
                browser.Kill(true);
            }
        }
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
