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
    public static readonly TimeSpan ControllerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>ZET's config.json, or null before ZET first saves it or while a read lands mid-write.</summary>
    public static JObject? SavedZetConfig(IntegrationFixture fixture)
    {
        try
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(fixture.Zet.IdentityDir, "config.json")));
        }
        catch (Exception ex) when (ex is IOException || ex is JsonReaderException)
        {
            return null;
        }
    }

    public static void WaitForPersistedMfaEnabled(AppiumSession session, IntegrationFixture fixture, string identifier,
        bool expected)
    {
        WaitUntil(session, $"config.json says MfaEnabled={expected} for {identifier}", ControllerTimeout, () =>
            SavedZetConfig(fixture)?["Identities"]?.Children<JObject>()
                .Any(identity => string.Equals((string?)identity["Identifier"], identifier, StringComparison.OrdinalIgnoreCase)
                    && (bool?)identity["MfaEnabled"] == expected) == true);
    }

    public static int IdentityRowCount(AppiumSession session) =>
        session.Driver.FindElements(IdentityRows).Count;

    public static By ToggleStatus(string identityName, string status) => By.XPath(
        $"{IdentityRowXPath(identityName)}//*[@AutomationId='ToggleStatus' and @Name='{status}']");

    private static bool IsUiCmdLine(RelayIpcServer.RecordedLine line, string uiLineFragment) =>
        line.From == "ui" && line.Pipe == "cmd" && line.Line.Contains(uiLineFragment);

    public static int UiCmdLineCount(RelayIpcServer relay, string uiLineFragment) =>
        relay.Recorded.Count(line => IsUiCmdLine(line, uiLineFragment));

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
    public const string StatusEvent = "\"Op\":\"status\"";
    public const string MfaEnrollmentRequiredEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_required\"";

    private static int LatestUiCmdLine(IReadOnlyList<RelayIpcServer.RecordedLine> recorded, string uiLineFragment)
    {
        for (int index = recorded.Count - 1; index >= 0; index--)
        {
            if (IsUiCmdLine(recorded[index], uiLineFragment))
                return index;
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
        recorded.Skip(sent + 1).FirstOrDefault(line => line.From == "zet" && line.Pipe == "cmd");

    public static JObject WaitForZetReplyTo(AppiumSession session, string uiLineFragment)
    {
        RelayIpcServer.RecordedLine? reply = null;
        WaitUntil(session, $"ZET replies to {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = session.Relay!.Recorded;
            reply = FindZetReplyTo(recorded, LatestUiCmdLine(recorded, uiLineFragment));
            return reply != null;
        });
        return JObject.Parse(reply!.Line);
    }

    public static byte[] CaptureBlurbOnReply(AppiumSession session, string uiLineFragment)
    {
        IReadOnlyList<RelayIpcServer.RecordedLine> recorded = session.Relay!.Recorded;
        int sent = LatestUiCmdLine(recorded, uiLineFragment);
        RelayIpcServer.RecordedLine reply = FindZetReplyTo(recorded, sent)
            ?? throw new InvalidOperationException($"ZET sent no reply to: {recorded[sent].Line}");
        return CaptureBlurb(session, reply.ReceivedAtUtc);
    }

    public static byte[] CaptureBlurbOnEvent(AppiumSession session, string uiLineFragment, string eventFragment)
    {
        RelayIpcServer.RecordedLine line = FindZetEventAfter(session.Relay!.Recorded, uiLineFragment, eventFragment)
            ?? throw new InvalidOperationException(
                $"ZET sent no event containing {eventFragment} after {uiLineFragment}");
        return CaptureBlurb(session, line.ReceivedAtUtc);
    }

    public static JObject SendAndWaitForZetReply(AppiumSession session, string uiLineFragment, Action send)
    {
        int sentBefore = UiCmdLineCount(session.Relay!, uiLineFragment);
        send();
        WaitUntil(session, $"the UI sends {uiLineFragment}", ControllerTimeout,
            () => UiCmdLineCount(session.Relay!, uiLineFragment) > sentBefore);
        return WaitForZetReplyTo(session, uiLineFragment);
    }

    private static JObject WaitForZetEvent(AppiumSession session, string description,
        Func<IReadOnlyList<RelayIpcServer.RecordedLine>, RelayIpcServer.RecordedLine?> findEvent)
    {
        RelayIpcServer.RecordedLine? zetEvent = null;
        WaitUntil(session, description, ControllerTimeout, () =>
        {
            zetEvent = findEvent(session.Relay!.Recorded);
            return zetEvent != null;
        });
        return JObject.Parse(zetEvent!.Line);
    }

    public static JObject WaitForZetEventAfter(AppiumSession session, string uiLineFragment, string eventFragment) =>
        WaitForZetEvent(session, $"ZET sends an event containing {eventFragment} after {uiLineFragment}",
            recorded => FindZetEventAfter(recorded, uiLineFragment, eventFragment));

    public static void AssertNoZetEventAfter(AppiumSession session, string uiLineFragment, string eventFragment,
        TimeSpan quiet)
    {
        Step.Log($"waiting {quiet.TotalSeconds}s for no ZET event containing {eventFragment} after {uiLineFragment}");
        Pause(quiet);
        RelayIpcServer.RecordedLine? unexpectedEvent = FindZetEventAfter(session.Relay!.Recorded, uiLineFragment, eventFragment);
        Assert.True(unexpectedEvent == null, $"ZET sent {unexpectedEvent?.Line} after {uiLineFragment}");
    }

    private static RelayIpcServer.RecordedLine? FindZetEventAfter(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        string uiLineFragment, string eventFragment) =>
        FindZetEventFrom(recorded, LatestUiCmdLine(recorded, uiLineFragment) + 1, eventFragment);

    private static RelayIpcServer.RecordedLine? FindZetEventFrom(IReadOnlyList<RelayIpcServer.RecordedLine> recorded,
        int fromLine, string eventFragment) =>
        recorded.Skip(fromLine)
            .FirstOrDefault(line => line.From == "zet" && line.Pipe == "event" && line.Line.Contains(eventFragment));

    /// <summary>Restart the fixture's ZET. Returns the relay line count before it, for the *From waits.</summary>
    public static async Task<int> RestartZet(IntegrationFixture fixture, AppiumSession session)
    {
        int before = session.Relay!.Recorded.Count;
        await fixture.RestartZetAsync();
        return before;
    }

    public static JObject WaitForZetEventFrom(AppiumSession session, int fromLine, string eventFragment) =>
        WaitForZetEvent(session, $"ZET sends an event containing {eventFragment} from relay line {fromLine}",
            recorded => FindZetEventFrom(recorded, fromLine, eventFragment));

    public static JObject StatusIdentity(JObject status, string identityName) =>
        StatusIdentityOrNull(status, identityName)
        ?? throw new InvalidDataException($"status event has no identity named {identityName}: {status}");

    public static JObject? StatusIdentityOrNull(JObject status, string identityName) =>
        status["Status"]!["Identities"]!.Children<JObject>().SingleOrDefault(identity => (string?)identity["Name"] == identityName);

    // The first added event names the identity file, a later one the name the signer's selector resolved to.
    public static void WaitForIdentityNamed(AppiumSession session, string uiLineFragment, string expectedName)
    {
        WaitUntil(session, $"ZET sends identity added named {expectedName} after {uiLineFragment}", ControllerTimeout, () =>
        {
            IReadOnlyList<RelayIpcServer.RecordedLine> recorded = session.Relay!.Recorded;
            return recorded.Skip(LatestUiCmdLine(recorded, uiLineFragment) + 1)
                .Where(line => line.From == "zet" && line.Pipe == "event" && line.Line.Contains(IdentityAddedEvent))
                .Any(line => (string?)JObject.Parse(line.Line)["Id"]?["Name"] == expectedName);
        });
    }

    public static JObject AssertMfaEventSucceeded(AppiumSession session, string uiLineFragment, string action)
    {
        JObject mfaEvent = WaitForZetEventAfter(session, uiLineFragment, $"\"Op\":\"mfa\",\"Action\":\"{action}\"");
        Assert.True((bool?)mfaEvent["Successful"] == true, $"ZET's mfa {action} event failed: {mfaEvent}");
        return mfaEvent;
    }

    public static void AssertMfaAuthenticated(AppiumSession session, string uiLineFragment)
    {
        JObject updated = WaitForZetEventAfter(session, uiLineFragment, "\"Op\":\"identity\",\"Action\":\"updated\"");
        Assert.True((bool?)updated["Id"]!["MfaEnabled"] == true && (bool?)updated["Id"]!["MfaNeeded"] == false,
            $"ZET's identity updated event is not MFA authenticated: {updated}");
    }

    public static string AddedIdentityFile(IntegrationFixture fixture) =>
        Path.Combine(fixture.Zet.IdentityDir, $"{AddedIdentityFileName}.json");

    // ZET rewrites the file after enrollment, so a read can hit a sharing violation or a half-written file.
    public static JObject ReadIdentityFile(string path)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when ((ex is IOException || ex is JsonReaderException) && DateTime.UtcNow < deadline)
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

    public static void EnterControllerUrl(AppiumSession session, string url)
    {
        ClickAddIdentityMenuItem(session, "With URL");
        TypeUntil(session, ById("ControllerURL"), url);
    }

    public static bool JoinOpensEnrollChoice(AppiumSession session)
    {
        int addsBefore = UiCmdLineCount(session.Relay!, AddIdentityLine);
        WaitForId(session, "JoinNetworkBtn").Click();
        // Not an XPath, because a whole-tree XPath polled through the signer GET loads the UI thread for seconds.
        WaitUntil(session, "the UI sends AddIdentity or opens the enrollment choice", ControllerTimeout,
            () => UiCmdLineCount(session.Relay!, AddIdentityLine) > addsBefore
                || FindByAccessibilityId(session, "AddIdentitySignerPicker") != null);
        return UiCmdLineCount(session.Relay!, AddIdentityLine) == addsBefore;
    }

    public static void JoinWithoutEnrollChoice(AppiumSession session) =>
        Assert.False(JoinOpensEnrollChoice(session), "the app opened the enrollment choice instead of sending AddIdentity");

    // A RadioButton's UIA Name is empty when its content is a panel, so each is found by its label's TextBlock.
    public static readonly By UserSessionRadio = By.XPath("//RadioButton[.//*[@Name='User session']]");
    public static readonly By DeviceCertificateRadio = By.XPath("//RadioButton[.//*[@Name='Device certificate']]");
    // Shown only when more than one signer can enroll.
    public static readonly By SignerPickerLabel = By.XPath("//*[@Name='Identity Provider']");

    public static void OpenEnrollChoice(AppiumSession session)
    {
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        Assert.True(JoinOpensEnrollChoice(session), "the app sent AddIdentity without offering the enrollment choice");
    }

    public static void AssertSentEnrollMode(AppiumSession session, string expected)
    {
        JObject sent = UiCommand(session.Relay!, AddIdentityLine);
        Assert.Equal(expected, (string?)sent["Data"]!["EnrollMode"]);
        Assert.Null((string?)sent["Data"]!["Provider"]);
    }

    // Both dialogs have a JoinNetworkBtn, and the URL dialog's stays in the tree until its fade out collapses it.
    public static IWebElement EnrollChoiceJoinButton(AppiumSession session)
    {
        WaitForGone(session, ById("ControllerURL"));
        return WaitForId(session, "JoinNetworkBtn");
    }

    public static string JoinFromEnrollChoice(AppiumSession session)
    {
        IWebElement joinButton = EnrollChoiceJoinButton(session);
        return IdpUrlFromReply(SendAndWaitForZetReply(session, AddIdentityLine, () => joinButton.Click()));
    }

    public static JObject JoinEnrolledToNone(AppiumSession session)
    {
        JoinWithoutEnrollChoice(session);
        JObject needsLogin = WaitForZetEventAfter(session, AddIdentityLine, NeedsExtLoginEvent);
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, AddIdentityLine)["Code"]);
        string? identifier = (string?)needsLogin["Id"]!["Identifier"];
        Assert.False(string.IsNullOrEmpty(identifier), $"needs_ext_login has no Identifier: {needsLogin}");
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        AssertUrlEnrolledToNoneIdentityFile(identifier!);
        WaitUntil(session, "the URL identity shows on the landing list", ControllerTimeout, () => IdentityRowCount(session) == 1);
        return needsLogin;
    }

    // The UI names every identity from a URL host_port, so a second add from the same URL collides with the first.
    public static async Task AssertSameNameRejected(AppiumSession session, string name, string step)
    {
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        JObject reply = SendAndWaitForZetReply(session, AddIdentityLine, () => WaitForId(session, "JoinNetworkBtn").Click());
        await VerifyStep(session, CaptureBlurbOnReply(session, AddIdentityLine), name, step);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(session));
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

    public static string LoginFromRow(AppiumSession session)
    {
        CloseBrowsers();
        return IdpUrlFromReply(
            SendAndWaitForZetReply(session, ExternalAuthLine, () => ClickAt(session, WaitFor(session, ExtAuthRequiredIcon))));
    }

    public static string IdpUrlFromReply(JObject reply)
    {
        Assert.Equal(0, (int?)reply["Code"]);
        string? url = (string?)reply["Data"]?["url"];
        Assert.False(string.IsNullOrEmpty(url), $"ZET's reply has no Data.url: {reply}");
        return url!;
    }

    // Log in before waiting on the browser, so one that never opens cannot leave ZET's login pending for 60s.
    public static async Task LogInAtIdP(AppiumSession session, string idpUrl, string email)
    {
        await Dex.DriveIdPFlowAsync(idpUrl, email);
        WaitUntil(session, "the app opens the IdP URL in the browser", ControllerTimeout, () =>
            System.Diagnostics.Process.GetProcessesByName("msedge").Any(browser => browser.MainWindowHandle != IntPtr.Zero));
        CloseBrowsers();
    }

    public static JObject WaitForNeedsExtLogin(AppiumSession session, string uiLineFragment)
    {
        JObject needsLogin = WaitForZetEventAfter(session, uiLineFragment, NeedsExtLoginEvent);
        Assert.True((bool?)needsLogin["Id"]!["NeedsExtAuth"] == true, $"needs_ext_login is not NeedsExtAuth: {needsLogin}");
        WaitForController(session, ExtAuthRequiredIcon, "the row asks for external auth");
        return needsLogin;
    }

    public static string JoinToEnrollmentUrl(AppiumSession session)
    {
        JoinWithoutEnrollChoice(session);
        return IdpUrlFromReply(WaitForZetReplyTo(session, AddIdentityLine));
    }

    public static void AssertGrantedServices(AppiumSession session, string uiLineFragment, IReadOnlyList<string> expected)
    {
        JObject bulkServiceEvent = WaitForZetEventAfter(session, uiLineFragment, "\"Op\":\"bulkservice\",\"Action\":\"updated\"");
        List<string> granted = bulkServiceEvent["AddedServices"]!.Select(service => (string)service["Name"]!).ToList();
        Assert.Equal(expected.OrderBy(serviceName => serviceName), granted.OrderBy(serviceName => serviceName));
    }

    public static JObject AssertEnrollmentAdded(AppiumSession session, string uiLineFragment)
    {
        JObject identityAddedEvent = WaitForZetEventAfter(session, uiLineFragment, IdentityAddedEvent);
        Assert.True((bool?)identityAddedEvent["Id"]!["Active"] == true, $"ZET's identity added event is not active: {identityAddedEvent}");
        Assert.True((bool?)identityAddedEvent["Id"]!["NeedsExtAuth"] == false, $"ZET's identity added event still needs ext auth: {identityAddedEvent}");
        return identityAddedEvent;
    }

    public static async Task<string> FinishEnrollToCert(AppiumSession session, string identityName, string authUrl)
    {
        await LogInAtIdP(session, authUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(session, AddIdentityLine)["Id"]!["Identifier"]!;
        AssertJwtEnrolledIdentityFile(identityFile);
        WaitUntil(session, "the enrolled identity shows on the landing list", ControllerTimeout,
            () => IdentityRowCount(session) == 1);
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

    public static void WaitForController(AppiumSession session, By target, string description) =>
        WaitUntil(session, description, ControllerTimeout, () => session.Driver.FindElements(target).Any(element => element.Displayed));

    // A Label's UIA Name is its whole Content, even when the row truncates it.
    public static void WaitForRowNamed(AppiumSession session, string identityName) =>
        WaitForController(session, By.XPath($"//*[@Name='{identityName}']"), $"the row is named {identityName}");

    public static async Task<AppiumSession> LaunchAsync(IntegrationFixture fixture, string testName)
    {
        await fixture.Zet.RemoveAllIdentitiesAsync();
        string controllerVersion = await fixture.Quickstart.ControllerVersionAsync();
        AppiumSession session = await AppiumSession.LaunchAgainstZetAsync(DefaultExePath(),
            IntegrationFixture.ZetDiscriminator,
            new[]
            {
                new VaryingText(By.XPath("//*[@AutomationId='IdUrl']"),
                    new Regex($"^{Quickstart.ZtApiPattern} at {Regex.Escape(controllerVersion)}$")),
                new VaryingText(By.XPath("//*[@AutomationId='IdServer']//*[@AutomationId='MainEdit']"),
                    new Regex($"^{Quickstart.ZtApiPattern}$")),
            },
            Path.Combine(TestResultsDir(), "captures", $"{testName}.jsonl"),
            UiLogPath(testName));
        return await PrepareOrDispose(session, launched =>
        {
            WaitForId(launched, "ConnectLabel");
            ClickAt(launched, WaitFor(launched, WelcomeClose));
            WaitForGone(launched, WelcomeClose);
            // A hidden docked window drops every child from the tree too, so check the landing page shows.
            WaitFor(launched, By.XPath("//Text[@Name='MAIN']"));
            PrepareTestWindow(launched);
        });
    }

    public static JObject AddIdentity(IntegrationFixture fixture, AppiumSession session, string identityName)
    {
        WriteTestJwt(fixture.Quickstart.GetJwtFromController(identityName));
        Step.Log($"adding {identityName} with its JWT from the controller");
        ClickAddIdentityWithJwt(session);
        WaitForController(session, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, AddIdentityLine)["Code"]);
        JObject identityAddedEvent = WaitForZetEventAfter(session, AddIdentityLine, IdentityAddedEvent);
        Assert.True((bool?)identityAddedEvent["Id"]!["Active"] == true, $"ZET's identity added event is not active: {identityAddedEvent}");
        AssertJwtEnrolledIdentityFile(AddedIdentityFile(fixture));
        SortAscending(session, "Name");
        return identityAddedEvent;
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

    public static void WaitForRecoveryCodes(AppiumSession session, IReadOnlyList<string> expected)
    {
        Assert.NotEmpty(expected);
        WaitUntil(session, $"the recovery codes read [{string.Join(",", expected)}]", ControllerTimeout,
            () => session.Driver.FindElements(RecoveryCodeBoxes).Select(codeBox => codeBox.Text).SequenceEqual(expected));
    }

    public static MfaEnrollment EnrollMfaToRecoveryCodes(IntegrationFixture fixture, AppiumSession session,
        string identityName)
    {
        JObject identityAddedEvent = AddIdentity(fixture, session, identityName);
        Assert.True((bool?)identityAddedEvent["Id"]!["MfaEnabled"] == false, $"MFA is enabled before EnableMFA: {identityAddedEvent}");
        WaitForZetEventAfter(session, AddIdentityLine, ControllerConnectedEvent);
        OpenMfaSetup(session, identityName);
        return CompleteMfaSetup(session);
    }

    public static MfaEnrollment CompleteMfaSetup(AppiumSession session)
    {
        ClickAt(session, WaitForId(session, "SecretButton"));
        string secret = WaitForId(session, "SecretCode").Text;
        TypeUntil(session, ById("SetupCode"), Totp.Compute(secret, DateTimeOffset.UtcNow));
        SendAndWaitForZetReply(session, VerifyMfaLine, () => WaitForId(session, "AuthSetupButton").Click());
        JObject enable = WaitForZetReplyTo(session, EnableMfaLine);
        Assert.Equal(0, (int?)enable["Code"]);
        Assert.False(string.IsNullOrEmpty((string?)enable["Data"]!["ProvisioningUrl"]), $"EnableMFA reply has no ProvisioningUrl: {enable}");
        Assert.NotEmpty(enable["Data"]!["RecoveryCodes"]!.Values<string>());
        Assert.True((bool?)enable["Data"]!["IsVerified"] == false, $"EnableMFA reply is verified before VerifyMFA: {enable}");
        JObject challenge = AssertMfaEventSucceeded(session, EnableMfaLine, "enrollment_challenge");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, VerifyMfaLine)["Code"]);
        AssertMfaAuthenticated(session, VerifyMfaLine);
        AssertMfaEventSucceeded(session, VerifyMfaLine, "enrollment_verification");
        List<string> codes = challenge["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList();
        WaitForRecoveryCodes(session, codes);
        return new MfaEnrollment(secret, codes);
    }

    public static readonly By MfaToggle = By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']");

    public static void OpenMfaSetup(AppiumSession session, string identityName)
    {
        OpenIdentityDetails(session, identityName);
        ClickAt(session, WaitFor(session, MfaToggle));
        WaitForController(session, ById("SetupCode"), "the MFA setup dialog opens");
    }

    public static void OpenRemovalPrompt(AppiumSession session, string identityName)
    {
        OpenIdentityDetails(session, identityName);
        ClickAt(session, WaitFor(session, MfaToggle));
        WaitForId(session, "AuthCode");
    }

    public static MfaEnrollment AddIdentityAndEnrollMfa(IntegrationFixture fixture, AppiumSession session,
        string identityName)
    {
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(fixture, session, identityName);
        ClickUntilGone(session, ById("CloseBlack"));
        CloseIdentityDetails(session);
        return enrollment;
    }

    // The app's UI thread can stall for seconds before it handles a click.
    private static readonly TimeSpan ToggleSendProbe = TimeSpan.FromSeconds(10);

    public static void ToggleOff(AppiumSession session, string identityName) =>
        ClickToggleUntilSent(session, identityName, OffLine, OnLine);

    public static void ToggleOn(AppiumSession session, string identityName) =>
        ClickToggleUntilSent(session, identityName, OnLine, OffLine);

    // A late first click plus a repeat would send both directions, so it clicks again only when none was sent.
    private static void ClickToggleUntilSent(AppiumSession session, string identityName, string sentLine, string oppositeLine)
    {
        RelayIpcServer relay = session.Relay!;
        int sentBefore = UiCmdLineCount(relay, sentLine);
        int oppositeBefore = UiCmdLineCount(relay, oppositeLine);
        Func<bool> sent = () => UiCmdLineCount(relay, sentLine) > sentBefore;
        DateTime deadline = DateTime.UtcNow + ControllerTimeout;
        int clicks = 0;
        while (!sent())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"The UI sent no {sentLine} after {clicks} clicks on {identityName}'s toggle over {ControllerTimeout.TotalSeconds}s. {SaveTimeoutDiagnostics(session)}");
            IWebElement toggle = WaitFor(session, InIdentityRow(identityName, "ToggleSwitch"));
            // The lookup waits on the UI thread, so a click it was stalled on has sent by now.
            if (sent())
                break;
            ClickAt(session, toggle);
            clicks++;
            DateTime probe = DateTime.UtcNow + ToggleSendProbe;
            while (!sent() && DateTime.UtcNow < probe)
                Pause(40);
        }
        Step.Log($"clicked {identityName}'s toggle until the UI sent {sentLine} ({clicks} clicks)");
        if (clicks <= 1)
            return;
        DateTime quietUntil = DateTime.UtcNow + ToggleSendProbe;
        while (DateTime.UtcNow < quietUntil)
        {
            if (UiCmdLineCount(relay, oppositeLine) > oppositeBefore)
                throw new InvalidOperationException(
                    $"A repeated click on {identityName}'s toggle also sent {oppositeLine}, so the identity toggled twice. {SaveTimeoutDiagnostics(session)}");
            Pause(40);
        }
    }

    public static void ToggleOffAndOn(AppiumSession session, string identityName)
    {
        ToggleOff(session, identityName);
        WaitForController(session, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        ToggleOn(session, identityName);
    }

    public static void TriggerReauthChallenge(AppiumSession session, string identityName)
    {
        ToggleOffAndOn(session, identityName);
        WaitForController(session, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
    }

    public static JObject SubmitFromRow(AppiumSession session, string name, string step, string identityName, string code)
    {
        ClickAt(session, WaitFor(session, InIdentityRow(identityName, "MfaRequired")));
        TypeUntil(session, ById("AuthCode"), code);
        SaveStep(session, name, step);
        return SendAndWaitForZetReply(session, SubmitMfaLine, () => WaitForId(session, "AuthButton").Click());
    }

    public static void AuthenticateFromRow(AppiumSession session, string name, string step, string identityName,
        string code)
    {
        // Waits on the relay first, so no UIA polling loads the UI thread while the reply and event land.
        Assert.Equal(0, (int?)SubmitFromRow(session, name, step, identityName, code)["Code"]);
        AssertMfaEventSucceeded(session, SubmitMfaLine, "mfa_auth_status");
        AssertMfaAuthenticated(session, SubmitMfaLine);
        WaitForGone(session, InIdentityRow(identityName, "MfaRequired"));
    }

    public static byte[] RejectFromRow(AppiumSession session, string name, string step, string identityName,
        string code)
    {
        JObject reply = SubmitFromRow(session, name, step, identityName, code);
        byte[] png = CaptureBlurbOnReply(session, SubmitMfaLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.NotEmpty(session.Driver.FindElements(ById("AuthCode")));
        Assert.NotEmpty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        return png;
    }
}
