using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(LegacyAuthCollection.Name)]
public class LegacyAuthPostureCheckTests
{
    private readonly LegacyAuthFixture _fixture;

    public LegacyAuthPostureCheckTests(LegacyAuthFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 180000)]
    public async Task MfaCheckDeniesUntilMfaEnrolled()
    {
        string name = nameof(MfaCheckDeniesUntilMfaEnrolled);
        const string identityName = "test_posture_added_mfa_client";
        const string serviceName = "test_posture_added_mfa_svc";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        await AddIdentityAndFailNewMfaCheck(_fixture, session, name, identityName, serviceName);

        int beforeEnroll = session.Relay!.Recorded.Count;
        EnrollMfaFromDetails(session);
        WaitForMfaCheck(session, beforeEnroll, serviceName, true);
        WaitForGone(session, WarnIcon);
        await VerifyStep(session, Capture(session), name, "04-passing-check-details");
        CloseIdentityDetails(session);
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaSetupNeeded")));
        await VerifyStep(session, Capture(session), name, "05-enrolled-row");
    }
}
