using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestExternalAuthSecondary in ziti-tunnel-sdk-c tests/integration/external_auth_test.go. The identity
/// enrolls with its JWT, and its auth policy also requires a login to the working signer before it connects.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthSecondaryTests
{
    private const string SecondaryPolicy = "test_ext_auth_secondary_policy";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthSecondaryTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
        // ZET sets this once for the whole test. Setting it again is a no-op.
        _fixture.Quickstart.SetAuthPolicySecondaryExtJwtSigner(SecondaryPolicy, _fixture.WorkingSignerId);
    }

    [Fact(Timeout = 120000)]
    public async Task SecondaryExtJwtCompletes()
    {
        string name = nameof(SecondaryExtJwtCompletes);
        const string identityName = "test_ext_auth_secondary_happy";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
            JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => ClickAddIdentityWithJwt(s));
            Assert.Equal(0, (int?)reply["Code"]);
            JObject needsLogin = WaitForNeedsExtLogin(s, AddIdentityLine);
            // ZET's needs_ext_login carries no Services key until the login.
            Assert.Null(needsLogin["Id"]!["Services"]);
            await VerifyStep(Capture(s), name, "01-identity-needs-ext-login");

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            AssertEnrollmentAdded(s, ExternalAuthLine);
            WaitForZetEventAfter(s, ExternalAuthLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
            AssertJwtEnrolledIdentityFile((string)needsLogin["Id"]!["Identifier"]!);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            WaitUntil(s, "the row stops asking for external auth", ControllerTimeout,
                () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));
            await VerifyStep(Capture(s), name, "02-identity-connected");
        }
        finally
        {
            CloseBrowsers();
        }
    }
}
