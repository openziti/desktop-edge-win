using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// A signer with only one of enroll-to-cert and enroll-to-token enabled sends that mode with no user session or device
/// certificate choice (openziti/desktop-edge-win#1092). No ZET test has a twin here: the choice is the UI's. The
/// BothEnabled twins in ExternalAuthSingleSignerTests cover the choice. Each test stops at the enrollment and denies
/// the IdP login it starts, since the other twins finish both enrollments.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class EnrollModeChoiceTests
{
    private readonly IntegrationFixture _fixture;

    public EnrollModeChoiceTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 90000)]
    public async Task CertOnlySignerSkipsEnrollChoice()
    {
        Trace.Begin();
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, nameof(CertOnlySignerSkipsEnrollChoice));
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            await DenyEnrollment(s, JoinToEnrollmentUrl(s));
            AssertSentEnrollMode(s, "cert");
        });
    }

    [Fact(Timeout = 90000)]
    public async Task TokenOnlySignerSkipsEnrollChoice()
    {
        Trace.Begin();
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, nameof(TokenOnlySignerSkipsEnrollChoice));
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            await DenyEnrollment(s, JoinToEnrollmentUrl(s));
            AssertSentEnrollMode(s, "token");
        });
    }
}
