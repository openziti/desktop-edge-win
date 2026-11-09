using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class PostureCheckTests
{
    private readonly IntegrationFixture _fixture;

    public PostureCheckTests(IntegrationFixture fixture, ITestOutputHelper output)
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
        AssertMfaCheckStaysFailingUnderOidc(session, beforeEnroll, serviceName);
        await VerifyStep(session, Capture(session), name, "04-failing-check-after-enrollment");
        CloseIdentityDetails(session);
        await VerifyStep(session, Capture(session), name, "05-enrolled-row");
    }
}
