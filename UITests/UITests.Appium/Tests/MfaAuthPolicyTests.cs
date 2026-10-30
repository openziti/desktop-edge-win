using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaAuthPolicyTests
{
    private const string TotpRequiredPolicy = "test_mfa_totp_policy";
    private static readonly TimeSpan NoChallengeQuiet = TimeSpan.FromSeconds(2);

    private readonly IntegrationFixture _fixture;

    public MfaAuthPolicyTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthUnderTotpRequiredPolicyAsksForCode()
    {
        string name = nameof(ReauthUnderTotpRequiredPolicyAsksForCode);
        const string identityName = "test_mfa_policy_required_enrolled";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        Step.Log($"adding {identityName}, whose auth policy requires TOTP");
        ClickAddIdentityWithJwt(session);
        // Partially authenticated until TOTP is enrolled, so the row keeps the file name until then.
        WaitForController(session, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, AddIdentityLine)["Code"]);
        WaitForZetEventAfter(session, AddIdentityLine, MfaEnrollmentRequiredEvent);
        ClickAt(session, WaitFor(session, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded")));
        WaitForController(session, ById("SetupCode"), "the MFA setup dialog opens");
        MfaEnrollment enrollment = CompleteMfaSetup(session);
        ClickUntilGone(session, ById("CloseBlack"));
        WaitForController(session, By.XPath($"//Text[@Name='{identityName}']"), $"the row shows {identityName}");
        SortAscending(session, "Name");

        TriggerReauthChallenge(session, identityName);
        WaitForZetEventAfter(session, OnLine, MfaAuthChallengeEvent);
        AssertNoZetEventAfter(session, OnLine, MfaEnrollmentRequiredEvent, NoChallengeQuiet);
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "01-authenticate-row");

        AuthenticateFromRow(session, name, "02-code-typed", identityName, Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        await VerifyStep(session, Capture(session), name, "03-authenticated-row");
    }

    [Fact(Timeout = 180000)]
    public async Task PolicyAddedForEnrolledIdentityKeepsWorking()
    {
        string name = nameof(PolicyAddedForEnrolledIdentityKeepsWorking);
        const string identityName = "test_mfa_policy_added_enrolled";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, session, identityName);
        string identifier = (string)WaitForZetEventAfter(session, AddIdentityLine, IdentityAddedEvent)["Id"]!["Identifier"]!;

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, TotpRequiredPolicy);
        // The UI never sends RefreshIdentity, so the harness does, standing in for ZET's own refresh.
        JObject refresh = await _fixture.Zet.SendCommandAsync(new JObject
        {
            ["Command"] = "RefreshIdentity",
            ["Data"] = new JObject { ["Identifier"] = identifier },
        });
        Assert.True((bool?)refresh["Success"] == true, $"RefreshIdentity failed: {refresh}");

        AssertNoZetEventAfter(session, VerifyMfaLine, MfaAuthChallengeEvent, NoChallengeQuiet);
        AssertNoZetEventAfter(session, VerifyMfaLine, MfaEnrollmentRequiredEvent, TimeSpan.Zero);
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "01-policy-added-row");
    }

    [Fact(Timeout = 120000)]
    public async Task PolicyAddedForUnenrolledIdentityAsksToEnroll()
    {
        string name = nameof(PolicyAddedForUnenrolledIdentityAsksToEnroll);
        const string identityName = "test_mfa_policy_added_unenrolled";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        WaitForZetEventAfter(session, AddIdentityLine, ControllerConnectedEvent);

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, TotpRequiredPolicy);
        ToggleOffAndOn(session, identityName);
        WaitForZetEventAfter(session, OnLine, MfaEnrollmentRequiredEvent);
        WaitForController(session, InIdentityRow(identityName, "MfaSetupNeeded"), "the row asks to set up MFA");
        AssertNoZetEventAfter(session, OnLine, MfaAuthChallengeEvent, NoChallengeQuiet);
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        await VerifyStep(session, Capture(session), name, "01-setup-needed-row");
    }
}
