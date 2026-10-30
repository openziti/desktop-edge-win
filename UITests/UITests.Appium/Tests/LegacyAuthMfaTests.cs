using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(LegacyAuthCollection.Name)]
public class LegacyAuthMfaTests
{
    private readonly LegacyAuthFixture _fixture;

    public LegacyAuthMfaTests(LegacyAuthFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    private static void AssertEnrolledAndOwesCode(AppiumSession session, int restarted, string identityName)
    {
        JObject identity = StatusIdentity(WaitForZetEventFrom(session, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == true, $"status after the restart should list MFA enrolled: {identity}");
        string identifier = (string)identity["Identifier"]!;
        // A ZET that authenticated before the app reconnected says so in the status, and its auth_challenge is lost.
        WaitUntil(session, $"ZET tells the app {identityName} owes a code", ControllerTimeout, () =>
            session.Relay!.Recorded.Skip(restarted).Where(line => line.From == "zet" && line.Pipe == "event").Any(line =>
                (line.Line.Contains(StatusEvent)
                    && (bool?)StatusIdentityOrNull(JObject.Parse(line.Line), identityName)?["MfaNeeded"] == true)
                || (line.Line.Contains(MfaAuthChallengeEvent) && string.Equals(
                    (string?)JObject.Parse(line.Line)["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))));
    }

    private async Task RestartToCodePrompt(AppiumSession session, string identityName)
    {
        int restarted = await RestartZet(_fixture, session);
        AssertEnrolledAndOwesCode(session, restarted, identityName);
        WaitForController(session, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
    }

    // A successful reauth reopens identity details.
    private static void AuthenticateToList(AppiumSession session, string name, string step, string identityName, string code)
    {
        AuthenticateFromRow(session, name, step, identityName, code);
        WaitForId(session, "IdentityDetailsClose");
        CloseIdentityDetails(session);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartOffersCodePromptForEnrolledIdentity()
    {
        string name = nameof(RestartOffersCodePromptForEnrolledIdentity);
        const string identityName = "test_legacy_mfa_restart_prompt";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        await RestartToCodePrompt(session, identityName);
        await VerifyStep(session, Capture(session), name, "01-authenticate-row");

        AuthenticateToList(session, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(session, Capture(session), name, "03-authenticated-row");
    }

    /// <summary>A toggle never reloads config.json, so the prompt comes from what the running ZET holds.</summary>
    [Fact(Timeout = 180000)]
    public async Task DisableEnableOffersCodePromptForEnrolledIdentity()
    {
        string name = nameof(DisableEnableOffersCodePromptForEnrolledIdentity);
        const string identityName = "test_legacy_mfa_toggle_prompt";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        TriggerReauthChallenge(session, identityName);
        WaitForZetEventAfter(session, OnLine, MfaAuthChallengeEvent);
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));

        AuthenticateToList(session, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), true);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartKeepsPersistedMfaEnabled()
    {
        string name = nameof(RestartKeepsPersistedMfaEnabled);
        const string identityName = "test_legacy_mfa_restart_state";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, session, identityName);

        await RestartToCodePrompt(session, identityName);
        WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), true);
    }

    // A TOTP code reused inside its window is rejected, so the second pass answers with a recovery code.
    [Fact(Timeout = 240000)]
    public async Task RepeatedRestartsKeepOfferingCodePrompt()
    {
        string name = nameof(RepeatedRestartsKeepOfferingCodePrompt);
        const string identityName = "test_legacy_mfa_repeat_restart";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        await RestartToCodePrompt(session, identityName);
        AuthenticateToList(session, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), true);

        await RestartToCodePrompt(session, identityName);
        AuthenticateToList(session, name, "02-recovery-code-typed", identityName, enrollment.RecoveryCodes[0]);
        WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), true);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartAcceptsRecoveryCode()
    {
        string name = nameof(RestartAcceptsRecoveryCode);
        const string identityName = "test_legacy_mfa_restart_recovery";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        await RestartToCodePrompt(session, identityName);
        AuthenticateToList(session, name, "01-recovery-code-typed", identityName, enrollment.RecoveryCodes[0]);
    }

    /// <summary>A ZET that keeps tearing its session down asks for a code again every ten seconds.</summary>
    [Fact(Timeout = 180000)]
    public async Task AcceptedCodeEndsThePrompting()
    {
        string name = nameof(AcceptedCodeEndsThePrompting);
        const string identityName = "test_legacy_mfa_quiet_after_code";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        await RestartToCodePrompt(session, identityName);
        AuthenticateToList(session, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));

        AssertNoZetEventAfter(session, SubmitMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(12));
        AssertNoZetEventAfter(session, SubmitMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        await VerifyStep(session, Capture(session), name, "02-still-authenticated-row");
    }

    [Fact(Timeout = 180000)]
    public async Task TwoIdentitiesKeepSeparateMfaState()
    {
        string name = nameof(TwoIdentitiesKeepSeparateMfaState);
        const string enrolledName = "test_legacy_mfa_pair_enrolled";
        const string plainName = "test_legacy_mfa_pair_plain";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, enrolledName);
        // The app names every JWT it adds after one fixed file, which ZET refuses twice, so the plain one goes to ZET.
        JObject addResp = await _fixture.Zet.SendCommandAsync(new JObject
        {
            ["Command"] = "AddIdentity",
            ["Data"] = new JObject
            {
                ["IdentityFilename"] = plainName,
                ["JwtContent"] = _fixture.Quickstart.GetJwtFromController(plainName),
                ["UseKeychain"] = false,
            },
        });
        Assert.True((bool?)addResp["Success"] == true, $"AddIdentity for {plainName} failed: {addResp}");
        WaitForController(session, By.XPath($"//Text[@Name='{plainName}']"), $"{plainName} shows on the landing list");

        int restarted = await RestartZet(_fixture, session);
        AssertEnrolledAndOwesCode(session, restarted, enrolledName);
        JObject plain = StatusIdentity(WaitForZetEventFrom(session, restarted, StatusEvent), plainName);
        Assert.True((bool?)plain["MfaEnabled"] == false && (bool?)plain["MfaNeeded"] == false,
            $"status after the restart should owe {plainName} no MFA: {plain}");
        WaitForController(session, InIdentityRow(enrolledName, "MfaRequired"), "the enrolled row asks to authenticate");
        Assert.Empty(session.Driver.FindElements(InIdentityRow(enrolledName, "MfaSetupNeeded")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(plainName, "MfaRequired")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(plainName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "01-restarted-rows");

        AuthenticateToList(session, name, "02-code-typed", enrolledName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(plainName, "MfaRequired")));
        await VerifyStep(session, Capture(session), name, "03-authenticated-rows");
    }

    // The state older clients left behind: config.json says an enrolled identity is not.
    [Fact(Timeout = 180000)]
    public async Task RecoversWhenPersistedMfaEnabledIsWrong()
    {
        string name = nameof(RecoversWhenPersistedMfaEnabledIsWrong);
        const string identityName = "test_legacy_mfa_corrupted_state";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);
        string identifier = AddedIdentityFile(_fixture);
        WaitForPersistedMfaEnabled(session, _fixture, identifier, true);

        string configPath = Path.Combine(_fixture.Zet.IdentityDir, "config.json");
        int restarted = session.Relay!.Recorded.Count;
        await _fixture.StopZetAsync();
        JObject config = JObject.Parse(File.ReadAllText(configPath));
        JObject persistedIdentity = config["Identities"]?.Children<JObject>().SingleOrDefault(configIdentity =>
                string.Equals((string?)configIdentity["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{configPath} has no identity {identifier}: {config}");
        persistedIdentity["MfaEnabled"] = false;
        File.WriteAllText(configPath, config.ToString());
        await _fixture.RestartStoppedZetAsync();

        JObject identity = StatusIdentity(WaitForZetEventFrom(session, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == false, $"status after the restart should list MFA not enrolled: {identity}");
        WaitForController(session, InIdentityRow(identityName, "MfaSetupNeeded"), "the row asks to set up MFA");
        await VerifyStep(session, Capture(session), name, "01-setup-needed-row");

        JObject enable = SendAndWaitForZetReply(session, EnableMfaLine,
            () => ClickAt(session, WaitFor(session, InIdentityRow(identityName, "MfaSetupNeeded"))));
        Assert.Equal(500, (int?)enable["Code"]);
        Assert.Contains("failed to authenticate", (string?)enable["Error"]);
        WaitForZetEventAfter(session, EnableMfaLine, MfaAuthChallengeEvent);
        // The failed enrollment_challenge raises "MFA setup failed to start", and auth_challenge follows within ms.
        byte[] setupFailed = CaptureBlurbOnEvent(session, EnableMfaLine,
            "\"Op\":\"mfa\",\"Action\":\"enrollment_challenge\"");
        WaitForController(session, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, setupFailed, name, "02-setup-failed-blurb");

        AuthenticateToList(session, name, "03-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(session, Capture(session), name, "04-authenticated-row");
    }

    [Fact(Timeout = 180000)]
    public async Task AdminRemovedMfaStopsPromptingAfterRestart()
    {
        string name = nameof(AdminRemovedMfaStopsPromptingAfterRestart);
        const string identityName = "test_legacy_mfa_admin_removed";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, session, identityName);
        Assert.True(_fixture.Quickstart.IdentityMfaEnabled(identityName), $"controller says {identityName} is not enrolled");
        await _fixture.Quickstart.RemoveIdentityMfaAsync(identityName);
        Assert.False(_fixture.Quickstart.IdentityMfaEnabled(identityName), $"controller still says {identityName} is enrolled");

        int restarted = await RestartZet(_fixture, session);
        WaitForController(session, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
        AssertNoZetEventAfter(session, VerifyMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(2));
        AssertNoZetEventAfter(session, VerifyMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        JObject identity = StatusIdentity(WaitForZetEventFrom(session, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaNeeded"] == false, $"status after the restart should owe no MFA: {identity}");
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "01-restarted-row");
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveMfaThenRestartStopsPrompting()
    {
        string name = nameof(RemoveMfaThenRestartStopsPrompting);
        const string identityName = "test_legacy_mfa_removed_then_restart";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);

        OpenRemovalPrompt(session, identityName);
        TypeUntil(session, ById("AuthCode"), enrollment.RecoveryCodes[0]);
        SaveStep(session, name, "01-code-typed");
        Assert.Equal(0, (int?)SendAndWaitForZetReply(session, RemoveMfaLine, () => WaitForId(session, "AuthButton").Click())["Code"]);
        AssertMfaEventSucceeded(session, RemoveMfaLine, "enrollment_remove");
        WaitForGone(session, ById("AuthCode"));
        CloseIdentityDetails(session);

        int restarted = await RestartZet(_fixture, session);
        JObject identity = StatusIdentity(WaitForZetEventFrom(session, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == false && (bool?)identity["MfaNeeded"] == false,
            $"status after the restart should owe no MFA: {identity}");
        WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), false);
        AssertNoZetEventAfter(session, RemoveMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(2));
        AssertNoZetEventAfter(session, RemoveMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "02-restarted-row");
    }

    // After the migration the code prompt comes from the controller, not from what ZET remembered.
    [Fact(Timeout = 300000)]
    public async Task MigratingToOidcKeepsTheIdentityEnrolled()
    {
        string name = nameof(MigratingToOidcKeepsTheIdentityEnrolled);
        const string identityName = "test_legacy_mfa_oidc_migration";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);
        await RestartToCodePrompt(session, identityName);

        await _fixture.MigrateToOidcAsync();
        try
        {
            await RestartToCodePrompt(session, identityName);
            await VerifyStep(session, Capture(session), name, "01-authenticate-row");

            AuthenticateToList(session, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
            await VerifyStep(session, Capture(session), name, "03-authenticated-row");
            Assert.True(_fixture.Quickstart.IdentityMfaEnabled(identityName),
                $"controller says {identityName} is not enrolled after the OIDC migration");
            WaitForPersistedMfaEnabled(session, _fixture, AddedIdentityFile(_fixture), true);
        }
        finally
        {
            await _fixture.RestoreLegacyAuthAsync();
        }
    }
}
