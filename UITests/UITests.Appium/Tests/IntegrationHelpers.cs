using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

public static class IntegrationHelpers
{
    // Steps that wait on a controller round trip, well past the mock's instant replies.
    public static readonly TimeSpan ControllerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>ZET's config.json, or null while a read lands mid-write.</summary>
    public static JObject? SavedZetConfig(IntegrationFixture fixture)
    {
        try
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(fixture.Zet.IdentityDir, "config.json")));
        }
        catch (Exception e) when (e is IOException || e is JsonReaderException)
        {
            return null;
        }
    }

    public static void WaitForPersistedMfaEnabled(AppiumSession s, IntegrationFixture fixture, string identifier,
        bool expected)
    {
        WaitUntil(s, $"config.json says MfaEnabled={expected} for {identifier}", ControllerTimeout, () =>
            SavedZetConfig(fixture)?["Identities"]?.Children<JObject>()
                .Any(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase)
                    && (bool?)i["MfaEnabled"] == expected) == true);
    }

    public static int IdentityRowCount(AppiumSession s) =>
        s.Driver.FindElements(By.XPath("//Custom[@ClassName='IdentityItem']")).Count;

    public static By ToggleStatus(string identityName, string status) => By.XPath(
        $"{IdentityRowXPath(identityName)}//*[@AutomationId='ToggleStatus' and @Name='{status}']");

    private static bool IsUiCmdLine(RelayIpcServer.RecordedLine line, string uiLineFragment) =>
        line.From == "ui" && line.Pipe == "cmd" && line.Line.Contains(uiLineFragment);

    public static int UiCmdLineCount(RelayIpcServer relay, string uiLineFragment) =>
        relay.Recorded.Count(r => IsUiCmdLine(r, uiLineFragment));

    public const string AddIdentityLine = "\"Command\":\"AddIdentity\"";
    public const string EnableMfaLine = "\"Command\":\"EnableMFA\"";
    public const string VerifyMfaLine = "\"Command\":\"VerifyMFA\"";
    public const string SubmitMfaLine = "\"Command\":\"SubmitMFA\"";
    public const string RemoveMfaLine = "\"Command\":\"RemoveMFA\"";
    public const string ExternalAuthLine = "\"Command\":\"ExternalAuth\"";
    public const string OffLine = "\"OnOff\":false";
    public const string OnLine = "\"OnOff\":true";

    public const string IdentityAddedEvent = "\"Op\":\"identity\",\"Action\":\"added\"";
    public const string NeedsExtLoginEvent = "\"Op\":\"identity\",\"Action\":\"needs_ext_login\"";
    public const string ControllerConnectedEvent = "\"Op\":\"controller\",\"Action\":\"connected\"";
    public const string ControllerDisconnectedEvent = "\"Op\":\"controller\",\"Action\":\"disconnected\"";
    public const string MfaAuthChallengeEvent = "\"Op\":\"mfa\",\"Action\":\"auth_challenge\"";
    public const string MfaEnrollmentChallengeEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_challenge\"";
    public const string StatusEvent = "\"Op\":\"status\"";
    public const string MfaEnrollmentRequiredEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_required\"";

    private static int LatestUiCmdLine(IReadOnlyList<RelayIpcServer.RecordedLine> recorded, string uiLineFragment)
    {
        for (int i = recorded.Count - 1; i >= 0; i--)
        {
            if (IsUiCmdLine(recorded[i], uiLineFragment))
                return i;
        }
        throw new InvalidOperationException($"the UI sent no cmd line containing {uiLineFragment}");
    }

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

    public static byte[] CaptureBlurbOnReply(AppiumSession s, string uiLineFragment) =>
        CaptureBlurb(s, ZetReplyLineTo(s.Relay!, uiLineFragment).ReceivedAtUtc);

    public static byte[] CaptureBlurbOnEvent(AppiumSession s, string uiLineFragment, string eventFragment)
    {
        RelayIpcServer.RecordedLine line = FindZetEventAfter(s.Relay!.Recorded, uiLineFragment, eventFragment)
            ?? throw new InvalidOperationException(
                $"ZET sent no event containing {eventFragment} after {uiLineFragment}");
        return CaptureBlurb(s, line.ReceivedAtUtc);
    }

    public static JObject SendAndWaitForZetReply(AppiumSession s, string uiLineFragment, Action send)
    {
        int sentBefore = UiCmdLineCount(s.Relay!, uiLineFragment);
        send();
        WaitUntil(s, $"the UI sends {uiLineFragment}", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, uiLineFragment) > sentBefore);
        return WaitForZetReplyTo(s, uiLineFragment);
    }

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

    public static void AssertNoZetEventAfter(AppiumSession s, string uiLineFragment, string eventFragment,
        TimeSpan quiet)
    {
        Step.Log($"waiting {quiet.TotalSeconds}s for no ZET event containing {eventFragment} after {uiLineFragment}");
        Pause(quiet);
        RelayIpcServer.RecordedLine? found = FindZetEventAfter(s.Relay!.Recorded, uiLineFragment, eventFragment);
        Assert.True(found == null, $"ZET sent {found?.Line} after {uiLineFragment}");
    }

    private static RelayIpcServer.RecordedLine? FindZetEventAfter(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        string uiLineFragment, string eventFragment) =>
        FindZetEventFrom(recorded, LatestUiCmdLine(recorded, uiLineFragment) + 1, eventFragment);

    private static RelayIpcServer.RecordedLine? FindZetEventFrom(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        int fromLine, string eventFragment) =>
        recorded.Skip(fromLine)
            .FirstOrDefault(r => r.From == "zet" && r.Pipe == "event" && r.Line.Contains(eventFragment));

    /// <summary>Restart the fixture's ZET. Returns the relay line count before it, for the *From waits.</summary>
    public static async Task<int> RestartZet(IntegrationFixture fixture, AppiumSession s)
    {
        int before = s.Relay!.Recorded.Count;
        await fixture.RestartZetAsync();
        return before;
    }

    public static JObject WaitForZetEventFrom(AppiumSession s, int fromLine, string eventFragment)
    {
        RelayIpcServer.RecordedLine? found = null;
        WaitUntil(s, $"ZET sends an event containing {eventFragment} from relay line {fromLine}", ControllerTimeout, () =>
        {
            found = FindZetEventFrom(s.Relay!.Recorded, fromLine, eventFragment);
            return found != null;
        });
        return JObject.Parse(found!.Line);
    }

    public static JObject StatusIdentity(JObject status, string identityName) =>
        StatusIdentityOrNull(status, identityName)
        ?? throw new InvalidDataException($"status event has no identity named {identityName}: {status}");

    public static JObject? StatusIdentityOrNull(JObject status, string identityName) =>
        status["Status"]!["Identities"]!.Children<JObject>().SingleOrDefault(i => (string?)i["Name"] == identityName);

    // The first added event names the identity file, a later one the name the signer's selector resolved to.
    public static void WaitForIdentityNamed(AppiumSession s, string uiLineFragment, string expectedName)
    {
        WaitUntil(s, $"ZET sends identity added named {expectedName} after {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = s.Relay!.Recorded;
            return recorded.Skip(LatestUiCmdLine(recorded, uiLineFragment) + 1)
                .Where(r => r.From == "zet" && r.Pipe == "event" && r.Line.Contains(IdentityAddedEvent))
                .Any(r => (string?)JObject.Parse(r.Line)["Id"]?["Name"] == expectedName);
        });
    }

    public static JObject AssertMfaEventSucceeded(AppiumSession s, string uiLineFragment, string action)
    {
        JObject mfa = WaitForZetEventAfter(s, uiLineFragment, $"\"Op\":\"mfa\",\"Action\":\"{action}\"");
        Assert.True((bool?)mfa["Successful"] == true, $"ZET's mfa {action} event failed: {mfa}");
        return mfa;
    }

    public static void AssertMfaAuthenticated(AppiumSession s, string uiLineFragment)
    {
        JObject updated = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"identity\",\"Action\":\"updated\"");
        Assert.True((bool?)updated["Id"]!["MfaEnabled"] == true && (bool?)updated["Id"]!["MfaNeeded"] == false,
            $"ZET's identity updated event is not MFA authenticated: {updated}");
    }

    public static string AddedIdentityFile(IntegrationFixture fixture) =>
        Path.Combine(fixture.Zet.IdentityDir, $"{AddedIdentityFileName}.json");

    private static readonly TimeSpan IdentityFileReadTimeout = TimeSpan.FromSeconds(5);

    // ZET rewrites the file after enrollment, so a read can hit a sharing violation or a half-written file.
    public static JObject ReadIdentityFile(string path)
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
                Pause(50);
            }
        }
    }

    public static void AssertJwtEnrolledIdentityFile(string path)
    {
        JObject file = ReadIdentityFile(path);
        foreach (string field in new[] { "ztAPI", "id.cert", "id.key", "id.ca" })
            Assert.False(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has no {field}");
    }

    public static void AssertUrlEnrolledToNoneIdentityFile(string path)
    {
        JObject file = ReadIdentityFile(path);
        foreach (string field in new[] { "ztAPI", "id.ca" })
            Assert.False(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has no {field}");
        foreach (string field in new[] { "id.cert", "id.key" })
            Assert.True(string.IsNullOrEmpty((string?)file.SelectToken(field)), $"identity file {path} has {field} after enroll-to-none");
    }

    public static void EnterControllerUrl(AppiumSession s, string url)
    {
        ClickAddIdentityWithUrl(s);
        TypeUntil(s, ById("ControllerURL"), url);
    }

    public static bool JoinOpensEnrollChoice(AppiumSession s)
    {
        int addsBefore = UiCmdLineCount(s.Relay!, AddIdentityLine);
        WaitForId(s, "JoinNetworkBtn").Click();
        // Not an XPath, because a whole-tree XPath polled through the signer GET loads the UI thread for seconds.
        WaitUntil(s, "the UI sends AddIdentity or opens the enrollment choice", ControllerTimeout,
            () => UiCmdLineCount(s.Relay!, AddIdentityLine) > addsBefore
                || FindByAccessibilityId(s, "AddIdentitySignerPicker") != null);
        return UiCmdLineCount(s.Relay!, AddIdentityLine) == addsBefore;
    }

    public static void JoinWithoutEnrollChoice(AppiumSession s) =>
        Assert.False(JoinOpensEnrollChoice(s), "the app opened the enrollment choice instead of sending AddIdentity");

    // A RadioButton's UIA Name is empty when its content is a panel, so each is found by its label's TextBlock.
    public static readonly By UserSessionRadio = By.XPath("//RadioButton[.//*[@Name='User session']]");
    public static readonly By DeviceCertificateRadio = By.XPath("//RadioButton[.//*[@Name='Device certificate']]");
    // Shown only when more than one signer can enroll.
    public static readonly By SignerPickerLabel = By.XPath("//*[@Name='Identity Provider']");

    public static void OpenEnrollChoice(AppiumSession s)
    {
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

    public static void AssertSentEnrollMode(AppiumSession s, string expected)
    {
        JObject sent = UiCommand(s.Relay!, AddIdentityLine);
        Assert.Equal(expected, (string?)sent["Data"]!["EnrollMode"]);
        Assert.Null((string?)sent["Data"]!["Provider"]);
    }

    public static string JoinFromEnrollChoice(AppiumSession s)
    {
        // Both dialogs have a JoinNetworkBtn, and the URL dialog's stays in the tree until its fade out collapses it.
        WaitForGone(s, ById("ControllerURL"));
        return IdpUrlFromReply(
            SendAndWaitForZetReply(s, AddIdentityLine, () => WaitForId(s, "JoinNetworkBtn").Click()));
    }

    public static JObject JoinEnrolledToNone(AppiumSession s)
    {
        JoinWithoutEnrollChoice(s);
        JObject needsLogin = WaitForZetEventAfter(s, AddIdentityLine, NeedsExtLoginEvent);
        Assert.Equal(0, (int?)WaitForZetReplyTo(s, AddIdentityLine)["Code"]);
        string? identifier = (string?)needsLogin["Id"]!["Identifier"];
        Assert.False(string.IsNullOrEmpty(identifier), $"needs_ext_login has no Identifier: {needsLogin}");
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        AssertUrlEnrolledToNoneIdentityFile(identifier!);
        WaitUntil(s, "the URL identity shows on the landing list", ControllerTimeout, () => IdentityRowCount(s) == 1);
        return needsLogin;
    }

    // The UI names every identity from a URL host_port, so a second add from the same URL collides with the first.
    public static async Task AssertSameNameRejected(AppiumSession s, string name, string step)
    {
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => WaitForId(s, "JoinNetworkBtn").Click());
        await VerifyStep(s, CaptureBlurbOnReply(s, AddIdentityLine), name, step);
        // The blurb shows no detail from ZET, so only the reply proves why the add failed.
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(s));
    }

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

    public static string LoginFromRow(AppiumSession s)
    {
        // The browser opened for an earlier IdP URL can cover the icon, and the click is a real mouse click.
        CloseBrowsers();
        return IdpUrlFromReply(
            SendAndWaitForZetReply(s, ExternalAuthLine, () => ClickAt(s, WaitFor(s, ExtAuthRequiredIcon))));
    }

    public static string IdpUrlFromReply(JObject reply)
    {
        Assert.Equal(0, (int?)reply["Code"]);
        string? url = (string?)reply["Data"]?["url"];
        Assert.False(string.IsNullOrEmpty(url), $"ZET's reply has no Data.url: {reply}");
        return url!;
    }

    // Log in before waiting on the browser, so one that never opens cannot leave ZET's login pending for 60s.
    public static async Task LogInAtIdP(AppiumSession s, string idpUrl, string email)
    {
        await Dex.DriveIdPFlowAsync(idpUrl, email);
        CloseBrowserOnceOpen(s);
    }

    private static void CloseBrowserOnceOpen(AppiumSession s)
    {
        WaitUntil(s, "the app opens the IdP URL in the browser", ControllerTimeout, () =>
            System.Diagnostics.Process.GetProcessesByName("msedge").Any(p => p.MainWindowHandle != IntPtr.Zero));
        CloseBrowsers();
    }

    public static JObject WaitForNeedsExtLogin(AppiumSession s, string uiLineFragment)
    {
        JObject needsLogin = WaitForZetEventAfter(s, uiLineFragment, NeedsExtLoginEvent);
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        WaitForController(s, ExtAuthRequiredIcon, "the row asks for external auth");
        return needsLogin;
    }

    public static string JoinToEnrollmentUrl(AppiumSession s)
    {
        JoinWithoutEnrollChoice(s);
        return IdpUrlFromReply(WaitForZetReplyTo(s, AddIdentityLine));
    }

    public static void AssertGrantedServices(AppiumSession s, string uiLineFragment, IReadOnlyList<string> expected)
    {
        JObject bulk = WaitForZetEventAfter(s, uiLineFragment, "\"Op\":\"bulkservice\",\"Action\":\"updated\"");
        List<string> granted = bulk["AddedServices"]!.Select(svc => (string)svc["Name"]!).ToList();
        Assert.Equal(expected.OrderBy(n => n), granted.OrderBy(n => n));
    }

    public static JObject AssertEnrollmentAdded(AppiumSession s, string uiLineFragment)
    {
        JObject added = WaitForZetEventAfter(s, uiLineFragment, IdentityAddedEvent);
        Assert.True((bool?)added["Id"]!["Active"] == true, $"ZET's identity added event is not active: {added}");
        Assert.True((bool?)added["Id"]!["NeedsExtAuth"] == false, $"ZET's identity added event still needs ext auth: {added}");
        return added;
    }

    public static async Task<string> FinishEnrollToCert(AppiumSession s, string identityName, string authUrl)
    {
        await LogInAtIdP(s, authUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(s, AddIdentityLine)["Id"]!["Identifier"]!;
        AssertJwtEnrolledIdentityFile(identityFile);
        WaitUntil(s, "the enrolled identity shows on the landing list", ControllerTimeout,
            () => IdentityRowCount(s) == 1);
        return identityFile;
    }

    // The test drives dex itself, so the app's browser windows only cover the app.
    public static void CloseBrowsers()
    {
        foreach (System.Diagnostics.Process browser in System.Diagnostics.Process.GetProcessesByName("msedge"))
        {
            using (browser)
            {
                browser.Kill(true);
                // Edge hands a new launch's URL to a running instance, so one still exiting loses the app's next URL.
                browser.WaitForExit();
            }
        }
    }

    public static void WaitForController(AppiumSession s, By by, string description) =>
        WaitUntil(s, description, ControllerTimeout, () => s.Driver.FindElements(by).Any(e => e.Displayed));

    // A Label's UIA Name is its whole Content, even when the row truncates it.
    public static void WaitForRowNamed(AppiumSession s, string identityName) =>
        WaitForController(s, By.XPath($"//*[@Name='{identityName}']"), $"the row is named {identityName}");

    public static async Task<AppiumSession> LaunchAsync(IntegrationFixture fixture, string testName)
    {
        await fixture.Zet.RemoveAllIdentitiesAsync();
        AppiumSession s = await AppiumSession.LaunchAgainstZetAsync(DefaultExePath(),
            IntegrationFixture.ZetDiscriminator,
            ControllerText(await fixture.Quickstart.ControllerVersionAsync()),
            Path.Combine(RepoRoot(), "UITests", "TestResults", "captures", $"{testName}.jsonl"),
            UiLogPath(testName));
        return await PrepareOrDispose(s, launched =>
        {
            WaitForId(launched, "ConnectLabel");
            DismissWelcome(launched);
            PrepareTestWindow(launched);
        });
    }

    private static IReadOnlyList<VaryingText> ControllerText(string controllerVersion) => new[]
    {
        new VaryingText(By.XPath("//*[@AutomationId='IdUrl']"),
            new Regex($"^{Quickstart.ZtApiPattern} at {Regex.Escape(controllerVersion)}$")),
        new VaryingText(By.XPath("//*[@AutomationId='IdServer']//*[@AutomationId='MainEdit']"),
            new Regex($"^{Quickstart.ZtApiPattern}$")),
    };

    public static JObject AddIdentity(IntegrationFixture fixture, AppiumSession s, string identityName)
    {
        WriteTestJwt(fixture.Quickstart.GetJwtFromController(identityName));
        Step.Log($"adding {identityName} with its JWT from the controller");
        ClickAddIdentityWithJwt(s);
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
        Assert.Equal(0, (int?)WaitForZetReplyTo(s, AddIdentityLine)["Code"]);
        JObject added = WaitForZetEventAfter(s, AddIdentityLine, IdentityAddedEvent);
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

    public static void WaitForRecoveryCodes(AppiumSession s, IReadOnlyList<string> expected)
    {
        Assert.NotEmpty(expected);
        WaitUntil(s, $"the recovery codes read [{string.Join(",", expected)}]", ControllerTimeout,
            () => s.Driver.FindElements(RecoveryCodeBoxes).Select(e => e.Text).SequenceEqual(expected));
    }

    public static List<string> ChallengeRecoveryCodes(JObject challenge) =>
        challenge["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList();

    public static MfaEnrollment EnrollMfaToRecoveryCodes(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        JObject added = AddIdentity(fixture, s, identityName);
        Assert.True((bool?)added["Id"]!["MfaEnabled"] == false, $"MFA is enabled before EnableMFA: {added}");
        WaitForZetEventAfter(s, AddIdentityLine, ControllerConnectedEvent);
        OpenMfaSetup(s, identityName);
        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        TypeUntil(s, ById("SetupCode"), Totp.Compute(secret, DateTimeOffset.UtcNow));
        SendAndWaitForZetReply(s, VerifyMfaLine, () => WaitForId(s, "AuthSetupButton").Click());
        List<string> codes = ChallengeRecoveryCodes(AssertMfaEnrollmentVerified(s));
        WaitForRecoveryCodes(s, codes);
        return new MfaEnrollment(secret, codes);
    }

    public static readonly By MfaToggle = By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']");

    public static void OpenMfaSetup(AppiumSession s, string identityName)
    {
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, MfaToggle));
        WaitForController(s, ById("SetupCode"), "the MFA setup dialog opens");
    }

    public static void OpenRemovalPrompt(AppiumSession s, string identityName)
    {
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, MfaToggle));
        WaitForId(s, "AuthCode");
    }

    public static JObject AssertMfaEnrollmentVerified(AppiumSession s)
    {
        // The setup dialog opens on the enrollment_challenge event, which can come before this reply.
        JObject enable = WaitForZetReplyTo(s, EnableMfaLine);
        Assert.Equal(0, (int?)enable["Code"]);
        Assert.False(string.IsNullOrEmpty((string?)enable["Data"]!["ProvisioningUrl"]), $"EnableMFA reply has no ProvisioningUrl: {enable}");
        Assert.NotEmpty(enable["Data"]!["RecoveryCodes"]!.Values<string>());
        Assert.True((bool?)enable["Data"]!["IsVerified"] == false, $"EnableMFA reply is verified before VerifyMFA: {enable}");
        JObject challenge = AssertMfaEventSucceeded(s, EnableMfaLine, "enrollment_challenge");
        Assert.Equal(0, (int?)WaitForZetReplyTo(s, VerifyMfaLine)["Code"]);
        AssertMfaAuthenticated(s, VerifyMfaLine);
        AssertMfaEventSucceeded(s, VerifyMfaLine, "enrollment_verification");
        return challenge;
    }

    public static MfaEnrollment AddIdentityAndEnrollMfa(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(fixture, s, identityName);
        ClickUntilGone(s, ById("CloseBlack"));
        CloseIdentityDetails(s);
        return enrollment;
    }

    public static MfaEnrollment AddIdentityAndEnrollMfaUnderPolicy(IntegrationFixture fixture, AppiumSession s,
        string identityName)
    {
        WriteTestJwt(fixture.Quickstart.GetJwtFromController(identityName));
        Step.Log($"adding {identityName}, whose auth policy requires TOTP");
        ClickAddIdentityWithJwt(s);
        // Partially authenticated until TOTP is enrolled, so the row keeps the file name until then.
        WaitForController(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.Equal(0, (int?)WaitForZetReplyTo(s, AddIdentityLine)["Code"]);
        WaitForZetEventAfter(s, AddIdentityLine, MfaEnrollmentRequiredEvent);
        ClickAt(s, WaitFor(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded")));
        WaitForController(s, ById("SetupCode"), "the MFA setup dialog opens");
        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        TypeUntil(s, ById("SetupCode"), Totp.Compute(secret, DateTimeOffset.UtcNow));
        SendAndWaitForZetReply(s, VerifyMfaLine, () => WaitForId(s, "AuthSetupButton").Click());
        List<string> codes = ChallengeRecoveryCodes(AssertMfaEnrollmentVerified(s));
        WaitForRecoveryCodes(s, codes);
        ClickUntilGone(s, ById("CloseBlack"));
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"the row shows {identityName}");
        SortByNameAscending(s);
        return new MfaEnrollment(secret, codes);
    }

    // The app's UI thread can stall for seconds before it handles a click.
    private static readonly TimeSpan ToggleSendProbe = TimeSpan.FromSeconds(10);

    public static void ToggleOff(AppiumSession s, string identityName) =>
        ClickToggleUntilSent(s, identityName, OffLine, OnLine);

    public static void ToggleOn(AppiumSession s, string identityName) =>
        ClickToggleUntilSent(s, identityName, OnLine, OffLine);

    // A late first click plus a repeat would send both directions, so it clicks again only when none was sent.
    private static void ClickToggleUntilSent(AppiumSession s, string identityName, string sentLine, string oppositeLine)
    {
        RelayIpcServer relay = s.Relay!;
        int sentBefore = UiCmdLineCount(relay, sentLine);
        int oppositeBefore = UiCmdLineCount(relay, oppositeLine);
        Func<bool> sent = () => UiCmdLineCount(relay, sentLine) > sentBefore;
        DateTime deadline = DateTime.UtcNow + ControllerTimeout;
        int clicks = 0;
        while (!sent())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"The UI sent no {sentLine} after {clicks} clicks on {identityName}'s toggle over {ControllerTimeout.TotalSeconds}s. {SaveTimeoutDiagnostics(s)}");
            IWebElement toggle = WaitFor(s, InIdentityRow(identityName, "ToggleSwitch"));
            // The lookup waits on the UI thread, so a click it was stalled on has sent by now.
            if (sent())
                break;
            ClickAt(s, toggle);
            clicks++;
            DateTime probe = DateTime.UtcNow + ToggleSendProbe;
            while (!sent() && DateTime.UtcNow < probe)
                Pause(40);
        }
        Step.Log($"clicked {identityName}'s toggle until the UI sent {sentLine} ({clicks} clicks)");
        if (clicks > 1)
            AssertNoUiCmdLineFor(s, oppositeLine, oppositeBefore, ToggleSendProbe, identityName);
    }

    private static void AssertNoUiCmdLineFor(AppiumSession s, string uiLineFragment, int countBefore, TimeSpan quiet,
        string identityName)
    {
        DateTime until = DateTime.UtcNow + quiet;
        while (DateTime.UtcNow < until)
        {
            if (UiCmdLineCount(s.Relay!, uiLineFragment) > countBefore)
                throw new InvalidOperationException(
                    $"A repeated click on {identityName}'s toggle also sent {uiLineFragment}, so the identity toggled twice. {SaveTimeoutDiagnostics(s)}");
            Pause(40);
        }
    }

    public static void ToggleOffAndOn(AppiumSession s, string identityName)
    {
        ToggleOff(s, identityName);
        WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        ToggleOn(s, identityName);
    }

    public static void TriggerReauthChallenge(AppiumSession s, string identityName)
    {
        ToggleOffAndOn(s, identityName);
        WaitForController(s, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
    }

    public static JObject SubmitFromRow(AppiumSession s, string name, string step, string identityName, string code)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "MfaRequired")));
        TypeUntil(s, ById("AuthCode"), code);
        SaveStep(s, name, step);
        return SendAndWaitForZetReply(s, SubmitMfaLine, () => WaitForId(s, "AuthButton").Click());
    }

    public static void AuthenticateFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        // Waits on the relay first, so no UIA polling loads the UI thread while the reply and event land.
        Assert.Equal(0, (int?)SubmitFromRow(s, name, step, identityName, code)["Code"]);
        AssertMfaEventSucceeded(s, SubmitMfaLine, "mfa_auth_status");
        AssertMfaAuthenticated(s, SubmitMfaLine);
        WaitForGone(s, InIdentityRow(identityName, "MfaRequired"));
    }

    public static byte[] RejectFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        JObject reply = SubmitFromRow(s, name, step, identityName, code);
        byte[] png = CaptureBlurbOnReply(s, SubmitMfaLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.NotEmpty(s.Driver.FindElements(ById("AuthCode")));
        Assert.NotEmpty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        return png;
    }
}
