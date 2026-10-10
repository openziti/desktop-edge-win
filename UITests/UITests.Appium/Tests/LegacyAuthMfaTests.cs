using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// MFA across ZET restarts on the legacy auth path, where the controller never says whether an identity has TOTP
/// enrolled. An enrolled identity must be asked for a code, not offered enrollment.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(LegacyAuthCollection.Name)]
public class LegacyAuthMfaTests
{
    private readonly LegacyAuthFixture _fixture;

    public LegacyAuthMfaTests(LegacyAuthFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Assert the first status from relay line restarted on lists the identity as enrolled, then wait until ZET tells
    /// the app it owes a code.
    /// </summary>
    private static void AssertEnrolledAndOwesCode(AppiumSession s, int restarted, string identityName)
    {
        JObject identity = StatusIdentity(WaitForZetEventFrom(s, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == true, $"status after the restart should list MFA enrolled: {identity}");
        string identifier = (string)identity["Identifier"]!;
        // A ZET that authenticated before the app reconnected says so in the status, and its auth_challenge is lost.
        // One that had not yet sends the auth_challenge after the status.
        WaitUntil(s, $"ZET tells the app {identityName} owes a code", ControllerTimeout, () =>
            s.Relay!.Recorded.Skip(restarted).Where(r => r.From == "zet" && r.Pipe == "event").Any(r =>
                (r.Line.Contains(StatusEvent)
                    && (bool?)StatusIdentityOrNull(JObject.Parse(r.Line), identityName)?["MfaNeeded"] == true)
                || (r.Line.Contains(MfaAuthChallengeEvent) && string.Equals(
                    (string?)JObject.Parse(r.Line)["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>Restart ZET and assert the app is told the identity is enrolled and owes a code.</summary>
    private async Task RestartToCodePrompt(AppiumSession s, string identityName)
    {
        int restarted = await RestartZet(_fixture, s);
        AssertEnrolledAndOwesCode(s, restarted, identityName);
        WaitForController(s, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
    }

    /// <summary>
    /// Authenticate from the row, then close the identity details a successful reauth reopens, ending on the landing
    /// list.
    /// </summary>
    private static void AuthenticateToList(AppiumSession s, string name, string step, string identityName, string code)
    {
        AuthenticateFromRow(s, name, step, identityName, code);
        WaitForId(s, "IdentityDetailsClose");
        CloseIdentityDetails(s);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartOffersCodePromptForEnrolledIdentity()
    {
        string name = nameof(RestartOffersCodePromptForEnrolledIdentity);
        const string identityName = "test_legacy_mfa_restart_prompt";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        await RestartToCodePrompt(s, identityName);
        await VerifyStep(Capture(s), name, "01-authenticate-row");

        AuthenticateToList(s, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(Capture(s), name, "03-authenticated-row");
    }

    /// <summary>A toggle never reloads config.json, so the prompt comes from what the running ZET holds.</summary>
    [Fact(Timeout = 180000)]
    public async Task DisableEnableOffersCodePromptForEnrolledIdentity()
    {
        string name = nameof(DisableEnableOffersCodePromptForEnrolledIdentity);
        const string identityName = "test_legacy_mfa_toggle_prompt";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        TriggerReauthChallenge(s, identityName);
        WaitForZetEventAfter(s, OnLine, MfaAuthChallengeEvent);
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));

        AuthenticateToList(s, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), true);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartKeepsPersistedMfaEnabled()
    {
        string name = nameof(RestartKeepsPersistedMfaEnabled);
        const string identityName = "test_legacy_mfa_restart_state";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);

        await RestartToCodePrompt(s, identityName);
        WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), true);
    }

    /// <summary>
    /// Two restarts prove ZET also wrote its state back correctly, since the second loads what the first saved. A TOTP
    /// code reused inside its window is rejected, so the second pass answers with a recovery code.
    /// </summary>
    [Fact(Timeout = 240000)]
    public async Task RepeatedRestartsKeepOfferingCodePrompt()
    {
        string name = nameof(RepeatedRestartsKeepOfferingCodePrompt);
        const string identityName = "test_legacy_mfa_repeat_restart";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        await RestartToCodePrompt(s, identityName);
        AuthenticateToList(s, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), true);

        await RestartToCodePrompt(s, identityName);
        AuthenticateToList(s, name, "02-recovery-code-typed", identityName, enrollment.RecoveryCodes[0]);
        WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), true);
    }

    [Fact(Timeout = 180000)]
    public async Task RestartAcceptsRecoveryCode()
    {
        string name = nameof(RestartAcceptsRecoveryCode);
        const string identityName = "test_legacy_mfa_restart_recovery";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        await RestartToCodePrompt(s, identityName);
        AuthenticateToList(s, name, "01-recovery-code-typed", identityName, enrollment.RecoveryCodes[0]);
    }

    /// <summary>A ZET that keeps tearing its session down asks for a code again every ten seconds.</summary>
    [Fact(Timeout = 180000)]
    public async Task AcceptedCodeEndsThePrompting()
    {
        string name = nameof(AcceptedCodeEndsThePrompting);
        const string identityName = "test_legacy_mfa_quiet_after_code";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        await RestartToCodePrompt(s, identityName);
        AuthenticateToList(s, name, "01-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));

        AssertNoZetEventAfter(s, SubmitMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(12));
        AssertNoZetEventAfter(s, SubmitMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        await VerifyStep(Capture(s), name, "02-still-authenticated-row");
    }

    [Fact(Timeout = 180000)]
    public async Task TwoIdentitiesKeepSeparateMfaState()
    {
        string name = nameof(TwoIdentitiesKeepSeparateMfaState);
        const string enrolledName = "test_legacy_mfa_pair_enrolled";
        const string plainName = "test_legacy_mfa_pair_plain";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, enrolledName);
        // The app names every JWT it adds after one fixed file, which ZET refuses twice, so the plain one goes to ZET.
        JObject added = await _fixture.Zet.SendCommandAsync(new JObject
        {
            ["Command"] = "AddIdentity",
            ["Data"] = new JObject
            {
                ["IdentityFilename"] = plainName,
                ["JwtContent"] = _fixture.Quickstart.GetJwtFromController(plainName),
                ["UseKeychain"] = false,
            },
        });
        Assert.True((bool?)added["Success"] == true, $"AddIdentity for {plainName} failed: {added}");
        WaitForController(s, By.XPath($"//Text[@Name='{plainName}']"), $"{plainName} shows on the landing list");

        int restarted = await RestartZet(_fixture, s);
        AssertEnrolledAndOwesCode(s, restarted, enrolledName);
        JObject plain = StatusIdentity(WaitForZetEventFrom(s, restarted, StatusEvent), plainName);
        Assert.True((bool?)plain["MfaEnabled"] == false && (bool?)plain["MfaNeeded"] == false,
            $"status after the restart should owe {plainName} no MFA: {plain}");
        WaitForController(s, InIdentityRow(enrolledName, "MfaRequired"), "the enrolled row asks to authenticate");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(enrolledName, "MfaSetupNeeded")));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(plainName, "MfaRequired")));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(plainName, "MfaSetupNeeded")));
        await VerifyStep(Capture(s), name, "01-restarted-rows");

        AuthenticateToList(s, name, "02-code-typed", enrolledName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(plainName, "MfaRequired")));
        await VerifyStep(Capture(s), name, "03-authenticated-rows");
    }

    /// <summary>Rewrite config.json so the identity file at identifier reads as never enrolled. ZET must be stopped.</summary>
    private static void PersistMfaDisabled(string configPath, string identifier)
    {
        JObject config = JObject.Parse(File.ReadAllText(configPath));
        JObject identity = config["Identities"]?.Children<JObject>().SingleOrDefault(i =>
                string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{configPath} has no identity {identifier}: {config}");
        identity["MfaEnabled"] = false;
        File.WriteAllText(configPath, config.ToString());
    }

    /// <summary>
    /// The state older clients left behind: config.json says an enrolled identity is not. ZET offers enrollment, which
    /// fails on the partially authenticated session, and a code prompt has to follow.
    /// </summary>
    [Fact(Timeout = 180000)]
    public async Task RecoversWhenPersistedMfaEnabledIsWrong()
    {
        string name = nameof(RecoversWhenPersistedMfaEnabledIsWrong);
        const string identityName = "test_legacy_mfa_corrupted_state";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        string identifier = AddedIdentityFile(_fixture);
        WaitForPersistedMfaEnabled(s, _fixture, identifier, true);

        string configPath = Path.Combine(_fixture.Zet.IdentityDir, "config.json");
        int restarted = s.Relay!.Recorded.Count;
        await _fixture.StopZetAsync();
        PersistMfaDisabled(configPath, identifier);
        await _fixture.RestartStoppedZetAsync();

        JObject identity = StatusIdentity(WaitForZetEventFrom(s, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == false, $"status after the restart should list MFA not enrolled: {identity}");
        WaitForController(s, InIdentityRow(identityName, "MfaSetupNeeded"), "the row asks to set up MFA");
        await VerifyStep(Capture(s), name, "01-setup-needed-row");

        JObject enable = SendAndWaitForZetReply(s, EnableMfaLine,
            () => ClickAt(s, WaitFor(s, InIdentityRow(identityName, "MfaSetupNeeded"))));
        Assert.Equal(500, (int?)enable["Code"]);
        Assert.Contains("failed to authenticate", (string?)enable["Error"]);
        WaitForZetEventAfter(s, EnableMfaLine, MfaAuthChallengeEvent);
        // The failed enrollment_challenge raises "MFA setup failed to start", and auth_challenge follows within ms.
        byte[] setupFailed = CaptureBlurbOnEvent(s, EnableMfaLine, MfaEnrollmentChallengeEvent);
        WaitForController(s, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(setupFailed, name, "02-setup-failed-blurb");

        AuthenticateToList(s, name, "03-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(Capture(s), name, "04-authenticated-row");
    }

    /// <summary>MFA an admin removed on the controller, while config.json still says enrolled.</summary>
    [Fact(Timeout = 180000)]
    public async Task AdminRemovedMfaStopsPromptingAfterRestart()
    {
        string name = nameof(AdminRemovedMfaStopsPromptingAfterRestart);
        const string identityName = "test_legacy_mfa_admin_removed";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        Assert.True(_fixture.Quickstart.IdentityMfaEnabled(identityName), $"controller says {identityName} is not enrolled");
        await _fixture.Quickstart.RemoveIdentityMfaAsync(identityName);
        Assert.False(_fixture.Quickstart.IdentityMfaEnabled(identityName), $"controller still says {identityName} is enrolled");

        int restarted = await RestartZet(_fixture, s);
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"{identityName} shows on the landing list");
        AssertNoZetEventAfter(s, VerifyMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(2));
        AssertNoZetEventAfter(s, VerifyMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        JObject identity = StatusIdentity(WaitForZetEventFrom(s, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaNeeded"] == false, $"status after the restart should owe no MFA: {identity}");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(Capture(s), name, "01-restarted-row");
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveMfaThenRestartStopsPrompting()
    {
        string name = nameof(RemoveMfaThenRestartStopsPrompting);
        const string identityName = "test_legacy_mfa_removed_then_restart";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);

        OpenRemovalPrompt(s, identityName);
        TypeUntil(s, ById("AuthCode"), enrollment.RecoveryCodes[0]);
        SaveStep(s, name, "01-code-typed");
        Assert.Equal(0, (int?)SendAndWaitForZetReply(s, RemoveMfaLine, () => WaitForId(s, "AuthButton").Click())["Code"]);
        AssertMfaEventSucceeded(s, RemoveMfaLine, "enrollment_remove");
        WaitForGone(s, ById("AuthCode"));
        CloseIdentityDetails(s);

        int restarted = await RestartZet(_fixture, s);
        JObject identity = StatusIdentity(WaitForZetEventFrom(s, restarted, StatusEvent), identityName);
        Assert.True((bool?)identity["MfaEnabled"] == false && (bool?)identity["MfaNeeded"] == false,
            $"status after the restart should owe no MFA: {identity}");
        WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), false);
        AssertNoZetEventAfter(s, RemoveMfaLine, MfaAuthChallengeEvent, TimeSpan.FromSeconds(2));
        AssertNoZetEventAfter(s, RemoveMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.FromSeconds(1));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(Capture(s), name, "02-restarted-row");
    }

    /// <summary>
    /// The migration support recommends: the identity stays enrolled, and the code prompt now comes from the controller
    /// rather than from what ZET remembered.
    /// </summary>
    [Fact(Timeout = 300000)]
    public async Task MigratingToOidcKeepsTheIdentityEnrolled()
    {
        string name = nameof(MigratingToOidcKeepsTheIdentityEnrolled);
        const string identityName = "test_legacy_mfa_oidc_migration";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        await RestartToCodePrompt(s, identityName);

        await _fixture.MigrateToOidcAsync();
        try
        {
            await RestartToCodePrompt(s, identityName);
            await VerifyStep(Capture(s), name, "01-authenticate-row");

            AuthenticateToList(s, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
            await VerifyStep(Capture(s), name, "03-authenticated-row");
            Assert.True(_fixture.Quickstart.IdentityMfaEnabled(identityName),
                $"controller says {identityName} is not enrolled after the OIDC migration");
            WaitForPersistedMfaEnabled(s, _fixture, AddedIdentityFile(_fixture), true);
        }
        finally
        {
            await _fixture.RestoreLegacyAuthAsync();
        }
    }
}
