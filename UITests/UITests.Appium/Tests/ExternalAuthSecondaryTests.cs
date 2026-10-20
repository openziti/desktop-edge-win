using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// Each identity belongs to one test, so a test that moves it to another auth policy never moves it back.
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthSecondaryTests
{
    private const string SecondaryPolicy = "test_ext_auth_secondary_policy";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthSecondaryTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
        // Setting it again is a no-op.
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
            JObject needsLogin = AddIdentityNeedingLogin(s, identityName);
            await VerifyStep(s, Capture(s), name, "01-identity-needs-ext-login");

            await LogInAtIdP(s, LoginFromRow(s), $"{identityName}@test.com");
            AssertEnrollmentAdded(s, ExternalAuthLine);
            WaitForConnected(s);
            AssertJwtEnrolledIdentityFile((string)needsLogin["Id"]!["Identifier"]!);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(s, Capture(s), name, "02-identity-connected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtReauthAsksForLogin()
    {
        string name = nameof(SecondaryExtJwtReauthAsksForLogin);
        const string identityName = "test_ext_auth_secondary_reauth";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            AddIdentityNeedingLogin(s, identityName);
            await LogInAtIdP(s, LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            await VerifyStep(s, Capture(s), name, "01-identity-connected");

            ToggleOff(s, identityName);
            WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
            await VerifyStep(s, Capture(s), name, "02-identity-disabled");
            ToggleOn(s, identityName);
            WaitForNeedsExtLogin(s, OnLine);
            await VerifyStep(s, Capture(s), name, "03-reauth-needs-ext-login");

            await LogInAtIdP(s, LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(s, Capture(s), name, "04-identity-reconnected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtPolicyAddedAsksForLogin()
    {
        string name = nameof(SecondaryExtJwtPolicyAddedAsksForLogin);
        const string identityName = "test_ext_auth_secondary_policy_added";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            AddIdentity(_fixture, s, identityName);
            WaitForZetEventAfter(s, AddIdentityLine, ControllerConnectedEvent);
            await VerifyStep(s, Capture(s), name, "01-identity-connected");

            _fixture.Quickstart.SetIdentityAuthPolicy(identityName, SecondaryPolicy);
            ToggleOffAndOn(s, identityName);
            WaitForNeedsExtLogin(s, OnLine);
            await VerifyStep(s, Capture(s), name, "02-policy-added-needs-ext-login");

            await LogInAtIdP(s, LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(s, Capture(s), name, "03-identity-reconnected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 150000)]
    public async Task SecondaryExtJwtPolicyRemovedConnectsWithoutLogin()
    {
        string name = nameof(SecondaryExtJwtPolicyRemovedConnectsWithoutLogin);
        const string identityName = "test_ext_auth_secondary_policy_removed";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            AddIdentityNeedingLogin(s, identityName);
            await VerifyStep(s, Capture(s), name, "01-identity-needs-ext-login");
            await LogInAtIdP(s, LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            await VerifyStep(s, Capture(s), name, "02-identity-connected");

            _fixture.Quickstart.SetIdentityAuthPolicy(identityName, "Default");
            ToggleOff(s, identityName);
            WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
            WaitForZetEventAfter(s, OffLine, ControllerDisconnectedEvent);
            ToggleOn(s, identityName);
            WaitForZetEventAfter(s, OnLine, ControllerConnectedEvent);
            WaitForController(s, ToggleStatus(identityName, "ENABLED"), $"{identityName} shows ENABLED");
            AssertGrantedServices(s, OnLine, new[] { "test_ext_auth_attr_user_svc" });
            Assert.DoesNotContain(s.Driver.FindElements(ExtAuthRequiredIcon), e => e.Displayed);
            await VerifyStep(s, Capture(s), name, "03-reconnected-without-login");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    private JObject AddIdentityNeedingLogin(AppiumSession s, string identityName)
    {
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => ClickAddIdentityWithJwt(s));
        Assert.Equal(0, (int?)reply["Code"]);
        JObject needsLogin = WaitForNeedsExtLogin(s, AddIdentityLine);
        // ZET's needs_ext_login carries no Services key until the login.
        Assert.Null(needsLogin["Id"]!["Services"]);
        return needsLogin;
    }

    private static void WaitForConnected(AppiumSession s)
    {
        WaitForZetEventAfter(s, ExternalAuthLine, ControllerConnectedEvent);
        WaitUntil(s, "the row stops asking for external auth", ControllerTimeout,
            () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));
    }
}
