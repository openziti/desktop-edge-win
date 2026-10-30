using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// Tests drive dex over HTTP from ZET's IdP URL, so the browser the app opens for it is closed unused.
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthSingleSignerTests : IDisposable
{
    private const string TotpPolicy = "test_mfa_totp_policy";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthSingleSignerTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    public void Dispose() => CloseBrowsers();

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneCompletes()
    {
        string name = nameof(EnrollToNoneCompletes);
        const string identityName = "test_ext_auth_none_happy";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        JoinEnrolledToNone(session);
        JObject sent = UiCommand(session.Relay!, AddIdentityLine);
        Assert.Null((string?)sent["Data"]!["EnrollMode"]);
        WaitForController(session, ExtAuthRequiredIcon, "the row asks for external auth");
        await VerifyStep(session, Capture(session), name, "01-identity-needs-ext-login");

        string loginUrl = LoginFromRow(session);
        await LogInAtIdP(session, loginUrl, $"{identityName}@test.com");
        JObject identityAddedEvent = AssertEnrollmentAdded(session, ExternalAuthLine);
        AssertUrlEnrolledToNoneIdentityFile((string)identityAddedEvent["Id"]!["Identifier"]!);
        WaitForRowLoggedIn(session);
        WaitForRowNamed(session, identityName);
        await VerifyStep(session, Capture(session), name, "02-identity-enrolled");
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneRejectsUnknownControllerIdentity()
    {
        string name = nameof(EnrollToNoneRejectsUnknownControllerIdentity);
        // No controller identity has this external id, so dex accepts the login and the controller rejects it.
        const string identityName = "test_ext_auth_unknown_identity";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        JoinEnrolledToNone(session);

        string loginUrl = LoginFromRow(session);
        await LogInAtIdP(session, loginUrl, $"{identityName}@test.com");
        WaitForZetEventAfter(session, ExternalAuthLine, ControllerDisconnectedEvent);
        await VerifyStep(session, Capture(session), name, "01-login-rejected");
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertCompletes()
    {
        string name = nameof(EnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            // One cert-only signer, so the app skips the enrollment choice (openziti/desktop-edge-win#1092).
            await CompleteEnrollToCert(session, "test_ext_auth_cert_happy");
            AssertSentEnrollMode(session, "cert");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesNameClaimSelector()
    {
        string name = nameof(EnrollToCertUsesNameClaimSelector);
        const string identityName = "test_ext_auth_name_selector";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, NameSelector = "/email" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(session, identityName);
            WaitForIdentityNamed(session, AddIdentityLine, $"{identityName}@test.com");
            WaitForRowNamed(session, $"{identityName}@test.com");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenCompletes()
    {
        string name = nameof(EnrollToTokenCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            // One token-only signer, so the app skips the enrollment choice (openziti/desktop-edge-win#1092).
            await CompleteEnrollToToken(session, "test_ext_auth_token_happy");
            AssertSentEnrollMode(session, "token");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesNameClaimSelector()
    {
        string name = nameof(EnrollToTokenUsesNameClaimSelector);
        const string identityName = "test_ext_auth_token_name_selector";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, NameSelector = "/email" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(session, identityName);
            WaitForIdentityNamed(session, ExternalAuthLine, $"{identityName}@test.com");
            WaitForRowNamed(session, $"{identityName}@test.com");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesAttrClaimSelector()
    {
        string name = nameof(EnrollToCertUsesAttrClaimSelector);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(session, "test_ext_auth_attr_selector");
            AssertGrantedServices(session, AddIdentityLine, new[] { "test_ext_auth_attr_user_svc" });
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesAttrClaimSelector()
    {
        string name = nameof(EnrollToTokenUsesAttrClaimSelector);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(session, "test_ext_auth_token_attr_selector");
            AssertGrantedServices(session, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
        });
    }

    // The only twin granting two services, so it also covers the details service list, its filter and service panel.
    [Fact(Timeout = 150000)]
    public async Task EnrollToCertUsesMultipleAttrClaims()
    {
        string name = nameof(EnrollToCertUsesMultipleAttrClaims);
        const string userService = "test_ext_auth_attr_user_svc";
        const string adminService = "test_ext_auth_attr_admin_svc";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(session, "test_ext_auth_multi_attr_selector");
            AssertGrantedServices(session, AddIdentityLine, new[] { userService, adminService });
            WaitForText(session, "ServiceCount", "2", ControllerTimeout);
            await VerifyStep(session, Capture(session), name, "01-identity-with-services");

            // The only row, opened without its name: the controller names it after dex's encoded subject claim.
            ClickUntil(session, IdentityRows, ById("IdentityDetailsClose"));
            WaitFor(session, By.XPath($"//*[@Name='{userService}']"));
            WaitFor(session, By.XPath($"//*[@Name='{adminService}']"));
            await VerifyStep(session, Capture(session), name, "02-service-list");

            TypeUntil(session, By.XPath("//*[@AutomationId='FilterServices']//Edit"), "admin");
            WaitForGone(session, By.XPath($"//*[@Name='{userService}']"));
            await VerifyStep(session, Capture(session), name, "03-filtered");

            ClickAt(session, WaitFor(session, By.XPath("//Image[@AutomationId='DetailIcon']")));
            Assert.Equal(adminService, WaitForId(session, "DetailName").Text);
            await VerifyStep(session, Capture(session), name, "04-service-panel");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesMultipleAttrClaims()
    {
        string name = nameof(EnrollToTokenUsesMultipleAttrClaims);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(session, "test_ext_auth_token_multi_attr_selector");
            AssertGrantedServices(session, ExternalAuthLine,
                new[] { "test_ext_auth_attr_user_svc", "test_ext_auth_attr_admin_svc" });
        });
    }

    [Fact(Timeout = 120000)]
    public async Task BothEnabledEnrollToCertCompletes()
    {
        string name = nameof(BothEnabledEnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            OpenEnrollChoice(session);
            IWebElement deviceCertificate = WaitFor(session, DeviceCertificateRadio);
            ClickAt(session, deviceCertificate);
            WaitUntil(session, "Device certificate is selected", AppResponseTimeout, () => deviceCertificate.Selected);
            Assert.False(WaitFor(session, UserSessionRadio).Selected, "User session stayed selected");
            await VerifyStep(session, Capture(session), name, "01-enroll-choice-device-certificate");
            await FinishEnrollToCert(session, "test_ext_auth_cert_both", JoinFromEnrollChoice(session));
            AssertSentEnrollMode(session, "cert");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task BothEnabledEnrollToTokenCompletes()
    {
        string name = nameof(BothEnabledEnrollToTokenCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            OpenEnrollChoice(session);
            Assert.True(WaitFor(session, UserSessionRadio).Selected, "User session is not the default enrollment");
            Assert.False(WaitFor(session, DeviceCertificateRadio).Selected, "Device certificate is selected by default");
            Assert.Empty(session.Driver.FindElements(SignerPickerLabel));
            await VerifyStep(session, Capture(session), name, "01-enroll-choice");

            await FinishEnrollToToken(session, "test_ext_auth_token_both", JoinFromEnrollChoice(session));
            AssertSentEnrollMode(session, "token");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneThenCertRejected()
    {
        string name = nameof(EnrollToNoneThenCertRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            EnterControllerUrl(session, Quickstart.UiControllerUrl);
            string identityFile = (string)JoinEnrolledToNone(session)["Id"]!["Identifier"]!;

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToCert = true });
            await AssertSameNameRejected(session, name, "01-add-failure-blurb");
            AssertSentEnrollMode(session, "cert");
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertThenNoneRejected()
    {
        string name = nameof(EnrollToCertThenNoneRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToCert(session, "test_ext_auth_cert_then_none");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, Quickstart.EnrollToNone);
            await AssertSameNameRejected(session, name, "01-add-failure-blurb");
            Assert.Null((string?)UiCommand(session.Relay!, AddIdentityLine)["Data"]!["EnrollMode"]);
            AssertJwtEnrolledIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertThenTokenRejected()
    {
        string name = nameof(EnrollToCertThenTokenRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToCert(session, "test_ext_auth_cert_then_token");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToToken = true });
            await AssertSameNameRejected(session, name, "01-add-failure-blurb");
            AssertSentEnrollMode(session, "token");
            AssertJwtEnrolledIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenThenCertRejected()
    {
        string name = nameof(EnrollToTokenThenCertRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToToken(session, "test_ext_auth_token_then_cert");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToCert = true });
            await AssertSameNameRejected(session, name, "01-add-failure-blurb");
            AssertSentEnrollMode(session, "cert");
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesEnrollAuthPolicy()
    {
        string name = nameof(EnrollToCertUsesEnrollAuthPolicy);
        const string identityName = "test_ext_auth_enroll_auth_policy";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AuthPolicy = TotpPolicy }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            EnterControllerUrl(session, Quickstart.UiControllerUrl);
            await LogInAtIdP(session, JoinToEnrollmentUrl(session), $"{identityName}@test.com");
            // A partial auth sends no identity added event, so the identifier comes from the mfa event.
            JObject enrollmentRequiredEvent = WaitForZetEventAfter(session, AddIdentityLine, MfaEnrollmentRequiredEvent);
            await AssertRowNeedsMfaSetup(session, name);
            AssertJwtEnrolledIdentityFile((string)enrollmentRequiredEvent["Identifier"]!);
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesEnrollAuthPolicy()
    {
        string name = nameof(EnrollToTokenUsesEnrollAuthPolicy);
        const string identityName = "test_ext_auth_token_enroll_auth_policy";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AuthPolicy = TotpPolicy }, async () =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            EnterControllerUrl(session, Quickstart.UiControllerUrl);
            await LogInAtIdP(session, JoinToEnrollmentUrl(session), $"{identityName}@test.com");
            string identityFile = (string)WaitForNeedsExtLogin(session, AddIdentityLine)["Id"]!["Identifier"]!;
            await LogInAtIdP(session, LoginFromRow(session), $"{identityName}@test.com");
            WaitForZetEventAfter(session, ExternalAuthLine, MfaEnrollmentRequiredEvent);
            await AssertRowNeedsMfaSetup(session, name);
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    // The row asks to set up MFA only while NeedsExtAuth is false, MfaNeeded true and MfaEnabled false.
    private static async Task AssertRowNeedsMfaSetup(AppiumSession session, string name)
    {
        WaitForController(session, ById("MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.DoesNotContain(session.Driver.FindElements(ExtAuthRequiredIcon), icon => icon.Displayed);
        Assert.Equal(1, IdentityRowCount(session));
        await VerifyStep(session, Capture(session), name, "01-setup-needed-row");
    }

    private static async Task<string> CompleteEnrollToCert(AppiumSession session, string identityName)
    {
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        return await FinishEnrollToCert(session, identityName, JoinToEnrollmentUrl(session));
    }

    private static async Task<string> CompleteEnrollToToken(AppiumSession session, string identityName)
    {
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        return await FinishEnrollToToken(session, identityName, JoinToEnrollmentUrl(session));
    }

    private static async Task<string> FinishEnrollToToken(AppiumSession session, string identityName, string enrollUrl)
    {
        await LogInAtIdP(session, enrollUrl, $"{identityName}@test.com");
        WaitForNeedsExtLogin(session, AddIdentityLine);
        string loginUrl = LoginFromRow(session);
        await LogInAtIdP(session, loginUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(session, ExternalAuthLine)["Id"]!["Identifier"]!;
        // An enroll-to-token identity file holds what an enroll-to-none one does.
        AssertUrlEnrolledToNoneIdentityFile(identityFile);
        WaitForRowLoggedIn(session);
        return identityFile;
    }

    private static void WaitForRowLoggedIn(AppiumSession session) =>
        WaitUntil(session, "the row no longer asks for external auth", ControllerTimeout,
            () => !session.Driver.FindElements(ExtAuthRequiredIcon).Any(icon => icon.Displayed));
}
