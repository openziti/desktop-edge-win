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

    public static int UiCmdLineCount(RelayIpcServer relay, string uiLineFragment) =>
        relay.Recorded.Count(r => r.From == "ui" && r.Pipe == "cmd" && r.Line.Contains(uiLineFragment));

    public const string AddIdentityLine = "\"Command\":\"AddIdentity\"";
    public const string EnableMfaLine = "\"Command\":\"EnableMFA\"";
    public const string VerifyMfaLine = "\"Command\":\"VerifyMFA\"";
    public const string SubmitMfaLine = "\"Command\":\"SubmitMFA\"";
    public const string RemoveMfaLine = "\"Command\":\"RemoveMFA\"";
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

    private static RelayIpcServer.RecordedLine? FindZetReplyTo(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        int sent) =>
        recorded.Skip(sent + 1).FirstOrDefault(r => r.From == "zet" && r.Pipe == "cmd");

    private static RelayIpcServer.RecordedLine ZetReplyLineTo(RelayIpcServer relay, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = relay.Recorded;
        int sent = LatestUiCmdLine(recorded, uiLineFragment);
        return FindZetReplyTo(recorded, sent)
            ?? throw new InvalidOperationException($"ZET sent no reply to: {recorded[sent].Line}");
    }

    /// <summary>ZET's reply on the cmd pipe to the latest UI command line containing uiLineFragment.</summary>
    public static JObject ZetReplyTo(RelayIpcServer relay, string uiLineFragment) =>
        JObject.Parse(ZetReplyLineTo(relay, uiLineFragment).Line);

    /// <summary>Wait for ZET's reply to the latest UI command line containing uiLineFragment, which must be sent.</summary>
    public static JObject WaitForZetReplyTo(AppiumSession s, string uiLineFragment)
    {
        RelayIpcServer.RecordedLine? reply = null;
        WaitUntil(s, $"ZET replies to {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            reply = FindZetReplyTo(recorded, LatestUiCmdLine(recorded, uiLineFragment));
            return reply != null;
        });
        return JObject.Parse(reply!.Line);
    }

    /// <summary>Capture the blurb the app raised on ZET's reply to the latest UI command line containing uiLineFragment.</summary>
    public static byte[] CaptureBlurbOnReply(AppiumSession s, string uiLineFragment) =>
        CaptureBlurb(s, ZetReplyLineTo(s.Relay!, uiLineFragment).ReceivedAtUtc);

    /// <summary>
    /// Capture the blurb the app raised on ZET's first event line containing eventFragment after the latest UI command
    /// line containing uiLineFragment.
    /// </summary>
    public static byte[] CaptureBlurbOnEvent(AppiumSession s, string uiLineFragment, string eventFragment)
    {
        RelayIpcServer.RecordedLine line = FindZetEventAfter(s.Relay!.Recorded, uiLineFragment, eventFragment)
            ?? throw new InvalidOperationException(
                $"ZET sent no event containing {eventFragment} after {uiLineFragment}");
        return CaptureBlurb(s, line.ReceivedAtUtc);
    }

    /// <summary>
    /// Run send, wait until the UI sends a new command line containing uiLineFragment, and return ZET's reply to it.
    /// </summary>
    public static JObject SendAndWaitForZetReply(AppiumSession s, string uiLineFragment, Action send)
    {
        int sentBefore = UiCmdLineCount(s.Relay!, uiLineFragment);
        send();
        WaitUntil(s, $"the UI sends {uiLineFragment}", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, uiLineFragment) > sentBefore);
        return WaitForZetReplyTo(s, uiLineFragment);
    }

    /// <summary>
    /// Wait for ZET's first event line containing eventFragment after the latest UI command line containing
    /// uiLineFragment, as ZET's tests wait for an event after their command.
    /// </summary>
    public static JObject WaitForZetEventAfter(AppiumSession s, string uiLineFragment, string eventFragment)
    {
        RelayIpcServer.RecordedLine? found = null;
        WaitUntil(s, $"ZET sends an event containing {eventFragment} after {uiLineFragment}", ControllerTimeout, () =>
        {
            found = FindZetEventAfter(s.Relay!.Recorded, uiLineFragment, eventFragment);
            return found != null;
        });
        return JObject.Parse(found!.Line);
    }

    private static RelayIpcServer.RecordedLine? FindZetEventAfter(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        string uiLineFragment, string eventFragment) =>
        recorded.Skip(LatestUiCmdLine(recorded, uiLineFragment) + 1)
            .FirstOrDefault(r => r.From == "zet" && r.Pipe == "event" && r.Line.Contains(eventFragment));

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
    /// Click Join Network on the URL dialog, then wait until the app either sends AddIdentity or opens the enrollment
    /// choice dialog instead. Returns whether the choice dialog opened.
    /// </summary>
    public static bool JoinOpensEnrollChoice(AppiumSession s)
    {
        int addsBefore = UiCmdLineCount(s.Relay!, AddIdentityLine);
        WaitForId(s, "JoinNetworkBtn").Click();
        // The app looks up the controller's ext-jwt signers before it does either. The dialog is collapsed, so out of
        // the UIA tree, until the app opens it. Not an XPath, because a whole-tree XPath polled through the signer GET
        // loads the UI thread for seconds.
        WaitUntil(s, "the UI sends AddIdentity or opens the enrollment choice", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, AddIdentityLine) > addsBefore
                || FindByAccessibilityId(s, "AddIdentitySignerPicker") != null);
        return UiCmdLineCount(s.Relay!, AddIdentityLine) == addsBefore;
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
    public static void OpenEnrollChoice(AppiumSession s)
    {
        PrepareTestWindow(s);
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
        SendAndWaitForZetReply(s, AddIdentityLine, () => WaitForId(s, "JoinNetworkBtn").Click());
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

    /// <summary>
    /// Enter the controller URL again and click Join Network, asserting ZET rejects the add as a duplicate: the UI names
    /// every identity from a URL host_port, so a second add from the same URL always collides with the first.
    /// </summary>
    public static async Task AssertSameNameRejected(AppiumSession s, string name, string step)
    {
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => WaitForId(s, "JoinNetworkBtn").Click());
        await VerifyStep(CaptureBlurbOnReply(s, AddIdentityLine), name, step);
        // The blurb shows no detail from ZET, so only the reply proves why the add failed.
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(s));
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
        JObject reply = SendAndWaitForZetReply(s, ExternalAuthLine, () => ClickAt(s, WaitFor(s, ExtAuthRequiredIcon)));
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

    /// <summary>ZET's completeEnrollToCert from ZET's AddIdentity reply on. Returns the identity file.</summary>
    public static async Task<string> FinishEnrollToCert(AppiumSession s, string identityName, string authUrl)
    {
        await Dex.DriveIdPFlowAsync(authUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(s, AddIdentityLine)["Id"]!["Identifier"]!;
        // ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-cert checks what the JWT one does.
        AssertJwtEnrolledIdentityFile(identityFile);
        WaitUntil(s, "the enrolled identity shows on the landing list", ControllerTimeout,
            () => IdentityRowCount(s) == 1);
        return identityFile;
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
    /// Empty the fixture's ZET and launch the app against it, recording to captures\testName and logging to
    /// logs\testName.
    /// </summary>
    public static async Task<AppiumSession> LaunchAsync(IntegrationFixture fixture, string testName)
    {
        await fixture.Zet.RemoveAllIdentitiesAsync();
        AppiumSession s = await AppiumSession.LaunchAgainstZetAsync(DefaultExePath(),
            IntegrationFixture.ZetDiscriminator,
            Path.Combine(RepoRoot(), "UITests", "TestResults", "captures", $"{testName}.jsonl"),
            UiLogPath(testName));
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
        Step.Log($"adding {identityName} with its JWT from the controller");
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
        OpenMfaSetup(s, identityName);
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

    // Identity details' MFA switch.
    public static readonly By MfaToggle = By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']");

    /// <summary>Open the identity's details and click its MFA switch, until the setup dialog asks for a code.</summary>
    public static void OpenMfaSetup(AppiumSession s, string identityName)
    {
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, MfaToggle));
        WaitForController(s, By.XPath("//*[@AutomationId='SetupCode']"), "the MFA setup dialog opens");
    }

    /// <summary>
    /// The asserts ZET's EnrollAndVerifyMFA makes from EnableMFA on, once the recovery codes show. Returns ZET's mfa
    /// enrollment_challenge event.
    /// </summary>
    public static JObject AssertMfaEnrollmentVerified(AppiumSession s)
    {
        JObject enable = ZetReplyTo(s.Relay!, EnableMfaLine);
        Assert.Equal(0, (int?)enable["Code"]);
        Assert.False(string.IsNullOrEmpty((string?)enable["Data"]!["ProvisioningUrl"]), $"EnableMFA reply has no ProvisioningUrl: {enable}");
        Assert.NotEmpty(enable["Data"]!["RecoveryCodes"]!.Values<string>());
        Assert.True((bool?)enable["Data"]!["IsVerified"] == false, $"EnableMFA reply is verified before VerifyMFA: {enable}");
        JObject challenge = AssertMfaEventSucceeded(s, EnableMfaLine, "enrollment_challenge");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, VerifyMfaLine)["Code"]);
        AssertMfaAuthenticated(s, VerifyMfaLine);
        AssertMfaEventSucceeded(s, VerifyMfaLine, "enrollment_verification");
        return challenge;
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

    /// <summary>Submit code from the row's MFA prompt, saving the typed code as step. Returns ZET's SubmitMFA reply.</summary>
    public static JObject SubmitFromRow(AppiumSession s, string name, string step, string identityName, string code)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "MfaRequired")));
        WaitForId(s, "AuthCode").SendKeys(code);
        SaveStep(s, name, step);
        return SendAndWaitForZetReply(s, SubmitMfaLine, () => WaitForId(s, "AuthButton").Click());
    }

    /// <summary>
    /// Submit code from the row's MFA prompt, wait for ZET to clear the lock, and assert what ZET's reauth tests do
    /// after a SubmitMFA that succeeds.
    /// </summary>
    public static void AuthenticateFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        // Waits on the relay first, so no UIA polling loads the UI thread while the reply and event land.
        Assert.Equal(0, (int?)SubmitFromRow(s, name, step, identityName, code)["Code"]);
        AssertMfaEventSucceeded(s, SubmitMfaLine, "mfa_auth_status");
        AssertMfaAuthenticated(s, SubmitMfaLine);
        // Clears on ZET's mfa_auth_status event.
        WaitForGone(s, InIdentityRow(identityName, "MfaRequired"));
    }

    /// <summary>
    /// Submit code from the row's MFA prompt, assert ZET rejects it and the prompt stays open, and return the
    /// capture taken while the failure blurb shows.
    /// </summary>
    public static byte[] RejectFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        // MFAScreen keeps the prompt open and shows "Authentication Failed" on a failed SubmitMFA reply.
        JObject reply = SubmitFromRow(s, name, step, identityName, code);
        byte[] png = CaptureBlurbOnReply(s, SubmitMfaLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.NotEmpty(s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']")));
        Assert.NotEmpty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        return png;
    }
}
