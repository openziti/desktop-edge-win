using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// An auth policy that requires TOTP, on an identity from the start or moved onto it by an administrator.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaAuthPolicyTests
{
    private const string TotpRequiredPolicy = "test_mfa_totp_policy";
    private static readonly TimeSpan NoChallengeQuiet = TimeSpan.FromSeconds(2);

    private readonly IntegrationFixture _fixture;

    public MfaAuthPolicyTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthUnderTotpRequiredPolicyAsksForCode()
    {
        string name = nameof(ReauthUnderTotpRequiredPolicyAsksForCode);
        const string identityName = "test_mfa_policy_required_enrolled";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfaUnderPolicy(_fixture, s, identityName);

        // Already enrolled, so the policy asks for a code, not for another enrollment.
        TriggerReauthChallenge(s, identityName);
        WaitForZetEventAfter(s, OnLine, MfaAuthChallengeEvent);
        AssertNoZetEventAfter(s, OnLine, MfaEnrollmentRequiredEvent, NoChallengeQuiet);
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(Capture(s), name, "01-authenticate-row");

        AuthenticateFromRow(s, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(Capture(s), name, "03-authenticated-row");
    }

    [Fact(Timeout = 180000)]
    public async Task PolicyAddedForEnrolledIdentityKeepsWorking()
    {
        string name = nameof(PolicyAddedForEnrolledIdentityKeepsWorking);
        const string identityName = "test_mfa_policy_added_enrolled";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        string identifier = (string)WaitForZetEventAfter(s, AddIdentityLine, IdentityAddedEvent)["Id"]!["Identifier"]!;

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, TotpRequiredPolicy);
        // The UI never sends RefreshIdentity, so the harness does, standing in for ZET's own refresh.
        JObject refresh = await _fixture.Zet.SendCommandAsync(new JObject
        {
            ["Command"] = "RefreshIdentity",
            ["Data"] = new JObject { ["Identifier"] = identifier },
        });
        Assert.True((bool?)refresh["Success"] == true, $"RefreshIdentity failed: {refresh}");

        // The session already satisfies the new policy.
        AssertNoZetEventAfter(s, VerifyMfaLine, MfaAuthChallengeEvent, NoChallengeQuiet);
        AssertNoZetEventAfter(s, VerifyMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.Zero);
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(Capture(s), name, "01-policy-added-row");
    }

    [Fact(Timeout = 120000)]
    public async Task PolicyAddedForUnenrolledIdentityAsksToEnroll()
    {
        string name = nameof(PolicyAddedForUnenrolledIdentityAsksToEnroll);
        const string identityName = "test_mfa_policy_added_unenrolled";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        WaitForZetEventAfter(s, AddIdentityLine, ControllerConnectedEvent);

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, TotpRequiredPolicy);
        ToggleOffAndOn(s, identityName);
        // Never enrolled, so it can no longer satisfy its policy and is asked to enroll.
        WaitForZetEventAfter(s, OnLine, MfaEnrollmentRequiredEvent);
        WaitForController(s, InIdentityRow(identityName, "MfaSetupNeeded"), "the row asks to set up MFA");
        AssertNoZetEventAfter(s, OnLine, MfaAuthChallengeEvent, NoChallengeQuiet);
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        await VerifyStep(Capture(s), name, "01-setup-needed-row");
    }
}
