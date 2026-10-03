using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestExternalAuthSecondary in ziti-tunnel-sdk-c tests/integration/external_auth_test.go. The identity
/// enrolls with its JWT, and its auth policy also requires a login to the working signer before it connects.
/// A subtest that moves its identity to another auth policy never moves it back, as in ZET, because each identity
/// belongs to one subtest.
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
            JObject needsLogin = AddIdentityNeedingLogin(s, identityName);
            await VerifyStep(Capture(s), name, "01-identity-needs-ext-login");

            await Dex.DriveIdPFlowAsync(LoginFromRow(s), $"{identityName}@test.com");
            AssertEnrollmentAdded(s, ExternalAuthLine);
            WaitForConnected(s);
            AssertJwtEnrolledIdentityFile((string)needsLogin["Id"]!["Identifier"]!);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(Capture(s), name, "02-identity-connected");
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
            await Dex.DriveIdPFlowAsync(LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            await VerifyStep(Capture(s), name, "01-identity-connected");

            // ZET's DisableEnableIdentity from the row.
            ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
            WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
            await VerifyStep(Capture(s), name, "02-identity-disabled");
            ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
            WaitForNeedsExtLogin(s, OnLine);
            await VerifyStep(Capture(s), name, "03-reauth-needs-ext-login");

            await Dex.DriveIdPFlowAsync(LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(Capture(s), name, "04-identity-reconnected");
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
            PrepareTestWindow(s);
            AddIdentity(_fixture, s, identityName);
            WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
            await VerifyStep(Capture(s), name, "01-identity-connected");

            _fixture.Quickstart.SetIdentityAuthPolicy(identityName, SecondaryPolicy);
            // ZET's DisableEnableIdentity from the row.
            ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
            WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
            ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
            WaitForNeedsExtLogin(s, OnLine);
            await VerifyStep(Capture(s), name, "02-policy-added-needs-ext-login");

            await Dex.DriveIdPFlowAsync(LoginFromRow(s), $"{identityName}@test.com");
            WaitForConnected(s);
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
            await VerifyStep(Capture(s), name, "03-identity-reconnected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    private const string OnLine = "\"OnOff\":true";

    /// <summary>
    /// Add identityName with its JWT from the controller, until ZET asks for its secondary login. Returns ZET's
    /// needs_ext_login event.
    /// </summary>
    private JObject AddIdentityNeedingLogin(AppiumSession s, string identityName)
    {
        // The browser the login opens takes focus, and a docked window hides when it loses focus.
        PrepareTestWindow(s);
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => ClickAddIdentityWithJwt(s));
        Assert.Equal(0, (int?)reply["Code"]);
        JObject needsLogin = WaitForNeedsExtLogin(s, AddIdentityLine);
        // ZET's needs_ext_login carries no Services key until the login.
        Assert.Null(needsLogin["Id"]!["Services"]);
        return needsLogin;
    }

    /// <summary>Wait for ZET's controller connected after the latest login, until the row stops asking for it.</summary>
    private static void WaitForConnected(AppiumSession s)
    {
        WaitForZetEventAfter(s, ExternalAuthLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
        WaitUntil(s, "the row stops asking for external auth", ControllerTimeout,
            () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));
    }
}
