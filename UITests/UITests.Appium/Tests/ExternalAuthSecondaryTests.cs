using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// Each identity belongs to one test, so a test that moves it to another auth policy never moves it back.
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthSecondaryTests : IDisposable
{
    private const string SecondaryPolicy = "test_ext_auth_secondary_policy";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthSecondaryTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
        // Setting it again is a no-op.
        _fixture.Quickstart.RunChecked("edge", "update", "auth-policy", SecondaryPolicy,
            "--secondary-req-ext-jwt-signer", _fixture.WorkingSignerId);
    }

    public void Dispose() => CloseBrowsers();

    [Fact(Timeout = 120000)]
    public async Task SecondaryExtJwtCompletes()
    {
        string name = nameof(SecondaryExtJwtCompletes);
        const string identityName = "test_ext_auth_secondary_happy";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        JObject needsLogin = AddIdentityNeedingLogin(session, identityName);
        await VerifyStep(session, Capture(session), name, "01-identity-needs-ext-login");

        await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
        AssertEnrollmentAdded(session, ExternalAuthLine);
        WaitForConnected(session);
        AssertJwtEnrolledIdentityFile((string)needsLogin["Id"]!["Identifier"]!);
        AssertGrantedServices(session, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
        await VerifyStep(session, Capture(session), name, "02-identity-connected");
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtReauthAsksForLogin()
    {
        string name = nameof(SecondaryExtJwtReauthAsksForLogin);
        const string identityName = "test_ext_auth_secondary_reauth";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityNeedingLogin(session, identityName);
        await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
        WaitForConnected(session);
        await VerifyStep(session, Capture(session), name, "01-identity-connected");

        ToggleOff(session, identityName);
        WaitForController(session, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        await VerifyStep(session, Capture(session), name, "02-identity-disabled");
        ToggleOn(session, identityName);
        WaitForNeedsExtLogin(session, OnLine);
        await VerifyStep(session, Capture(session), name, "03-reauth-needs-ext-login");

        await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
        WaitForConnected(session);
        AssertGrantedServices(session, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
        await VerifyStep(session, Capture(session), name, "04-identity-reconnected");
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtPolicyAddedAsksForLogin()
    {
        string name = nameof(SecondaryExtJwtPolicyAddedAsksForLogin);
        const string identityName = "test_ext_auth_secondary_policy_added";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        WaitForZetEventAfter(session, AddIdentityLine, ControllerConnectedEvent);
        await VerifyStep(session, Capture(session), name, "01-identity-connected");

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, SecondaryPolicy);
        ToggleOffAndOn(session, identityName);
        WaitForNeedsExtLogin(session, OnLine);
        await VerifyStep(session, Capture(session), name, "02-policy-added-needs-ext-login");

        await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
        WaitForConnected(session);
        AssertGrantedServices(session, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
        await VerifyStep(session, Capture(session), name, "03-identity-reconnected");
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtPolicyRemovedConnectsWithoutLogin()
    {
        string name = nameof(SecondaryExtJwtPolicyRemovedConnectsWithoutLogin);
        const string identityName = "test_ext_auth_secondary_policy_removed";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityNeedingLogin(session, identityName);
        await VerifyStep(session, Capture(session), name, "01-identity-needs-ext-login");
        await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
        WaitForConnected(session);
        await VerifyStep(session, Capture(session), name, "02-identity-connected");

        _fixture.Quickstart.SetIdentityAuthPolicy(identityName, "Default");
        ToggleOff(session, identityName);
        WaitForController(session, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        WaitForZetEventAfter(session, OffLine, ControllerDisconnectedEvent);
        ToggleOn(session, identityName);
        WaitForZetEventAfter(session, OnLine, ControllerConnectedEvent);
        WaitForController(session, ToggleStatus(identityName, "ENABLED"), $"{identityName} shows ENABLED");
        AssertGrantedServices(session, OnLine, new[] { "test_ext_auth_attr_user_svc" });
        Assert.DoesNotContain(session.Driver.FindElements(ExtAuthRequiredIcon), icon => icon.Displayed);
        await VerifyStep(session, Capture(session), name, "03-reconnected-without-login");
    }

    private JObject AddIdentityNeedingLogin(AppiumSession session, string identityName)
    {
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        JObject reply = SendAndWaitForZetReply(session, AddIdentityLine, () => ClickAddIdentityWithJwt(session));
        Assert.Equal(0, (int?)reply["Code"]);
        JObject needsLogin = WaitForNeedsExtLogin(session, AddIdentityLine);
        Assert.Null(needsLogin["Id"]!["Services"]);
        return needsLogin;
    }

    private static void WaitForConnected(AppiumSession session)
    {
        WaitForZetEventAfter(session, ExternalAuthLine, ControllerConnectedEvent);
        WaitUntil(session, "the row stops asking for external auth", ControllerTimeout,
            () => !session.Driver.FindElements(ExtAuthRequiredIcon).Any(icon => icon.Displayed));
    }
}
