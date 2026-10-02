using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestExternalAuthSingleSigner in ziti-tunnel-sdk-c tests/integration/external_auth_test.go. Like ZET's
/// test, each twin reads the IdP URL from ZET's reply and drives dex over HTTP, so the browser the app opens for that
/// URL is closed unused. Each twin detaches the window first, because that browser takes focus and a docked window
/// hides when it loses focus.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthSingleSignerTests
{
    private const string TotpPolicy = "test_mfa_totp_policy";
    private const string EnrollmentRequiredEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_required\"";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthSingleSignerTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneCompletes()
    {
        string name = nameof(EnrollToNoneCompletes);
        const string identityName = "test_ext_auth_none_happy";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            JoinEnrolledToNone(s);
            // The working signer enrolls to neither, so the app sends no mode.
            JObject sent = UiCommand(s.Relay!, AddIdentityLine);
            Assert.Null((string?)sent["Data"]!["EnrollMode"]);
            WaitForController(s, ExtAuthRequiredIcon, "the row asks for external auth");
            await VerifyStep(Capture(s), name, "01-identity-needs-ext-login");

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
            AssertUrlEnrolledToNoneIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitForRowLoggedIn(s);
            await VerifyStep(Capture(s), name, "02-identity-enrolled");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneRejectsUnknownControllerIdentity()
    {
        string name = nameof(EnrollToNoneRejectsUnknownControllerIdentity);
        // The fixture has no controller identity with this external id, so dex accepts the login and the controller
        // rejects it.
        const string identityName = "test_ext_auth_unknown_identity";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            JoinEnrolledToNone(s);

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            WaitForZetEventAfter(s, ExternalAuthLine, "\"Op\":\"controller\",\"Action\":\"disconnected\"");
            await VerifyStep(Capture(s), name, "01-login-rejected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertCompletes()
    {
        string name = nameof(EnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            // One cert-only signer, so the app skips the enrollment choice (openziti/desktop-edge-win#1092).
            await CompleteEnrollToCert(s, "test_ext_auth_cert_happy");
            AssertSentEnrollMode(s, "cert");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesNameClaimSelector()
    {
        string name = nameof(EnrollToCertUsesNameClaimSelector);
        const string identityName = "test_ext_auth_name_selector";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, NameSelector = "/email" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(s, identityName);
            WaitForIdentityNamed(s, AddIdentityLine, $"{identityName}@test.com");
            WaitForRowNamed(s, $"{identityName}@test.com");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenCompletes()
    {
        string name = nameof(EnrollToTokenCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            // One token-only signer, so the app skips the enrollment choice (openziti/desktop-edge-win#1092).
            await CompleteEnrollToToken(s, "test_ext_auth_token_happy");
            AssertSentEnrollMode(s, "token");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesNameClaimSelector()
    {
        string name = nameof(EnrollToTokenUsesNameClaimSelector);
        const string identityName = "test_ext_auth_token_name_selector";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, NameSelector = "/email" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(s, identityName);
            WaitForIdentityNamed(s, ExternalAuthLine, $"{identityName}@test.com");
            WaitForRowNamed(s, $"{identityName}@test.com");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesAttrClaimSelector()
    {
        string name = nameof(EnrollToCertUsesAttrClaimSelector);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(s, "test_ext_auth_attr_selector");
            AssertGrantedServices(s, AddIdentityLine, new[] { "test_ext_auth_attr_user_svc" });
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesAttrClaimSelector()
    {
        string name = nameof(EnrollToTokenUsesAttrClaimSelector);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(s, "test_ext_auth_token_attr_selector");
            AssertGrantedServices(s, ExternalAuthLine, new[] { "test_ext_auth_attr_user_svc" });
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesMultipleAttrClaims()
    {
        string name = nameof(EnrollToCertUsesMultipleAttrClaims);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(s, "test_ext_auth_multi_attr_selector");
            AssertGrantedServices(s, AddIdentityLine,
                new[] { "test_ext_auth_attr_user_svc", "test_ext_auth_attr_admin_svc" });
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesMultipleAttrClaims()
    {
        string name = nameof(EnrollToTokenUsesMultipleAttrClaims);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AttrSelector = "/groups" }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToToken(s, "test_ext_auth_token_multi_attr_selector");
            AssertGrantedServices(s, ExternalAuthLine,
                new[] { "test_ext_auth_attr_user_svc", "test_ext_auth_attr_admin_svc" });
        });
    }

    // ZET's bothEnrollFlowsCompleteWhenBothEnabled, split at its two RunWithTimeout halves.
    [Fact(Timeout = 120000)]
    public async Task BothEnabledEnrollToCertCompletes()
    {
        string name = nameof(BothEnabledEnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            OpenEnrollChoice(s);
            ChooseDeviceCertificate(s);
            await VerifyStep(Capture(s), name, "01-enroll-choice-device-certificate");
            await FinishEnrollToCert(s, "test_ext_auth_cert_both", JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "cert");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task BothEnabledEnrollToTokenCompletes()
    {
        string name = nameof(BothEnabledEnrollToTokenCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            OpenEnrollChoice(s);
            Assert.True(WaitFor(s, UserSessionRadio).Selected, "User session is not the default enrollment");
            Assert.False(WaitFor(s, DeviceCertificateRadio).Selected, "Device certificate is selected by default");
            // One signer, so there is no provider to pick.
            Assert.Empty(s.Driver.FindElements(SignerPickerLabel));
            await VerifyStep(Capture(s), name, "01-enroll-choice");

            await FinishEnrollToToken(s, "test_ext_auth_token_both", JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "token");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneThenCertRejected()
    {
        string name = nameof(EnrollToNoneThenCertRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            string identityFile = (string)JoinEnrolledToNone(s)["Id"]!["Identifier"]!;

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToCert = true });
            await AssertSameNameRejected(s, name, "01-add-failure-blurb");
            AssertSentEnrollMode(s, "cert");
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertThenNoneRejected()
    {
        string name = nameof(EnrollToCertThenNoneRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToCert(s, "test_ext_auth_cert_then_none");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, Quickstart.EnrollToNone);
            await AssertSameNameRejected(s, name, "01-add-failure-blurb");
            Assert.Null((string?)UiCommand(s.Relay!, AddIdentityLine)["Data"]!["EnrollMode"]);
            AssertJwtEnrolledIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertThenTokenRejected()
    {
        string name = nameof(EnrollToCertThenTokenRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToCert(s, "test_ext_auth_cert_then_token");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToToken = true });
            await AssertSameNameRejected(s, name, "01-add-failure-blurb");
            AssertSentEnrollMode(s, "token");
            AssertJwtEnrolledIdentityFile(identityFile);
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenThenCertRejected()
    {
        string name = nameof(EnrollToTokenThenCertRejected);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            string identityFile = await CompleteEnrollToToken(s, "test_ext_auth_token_then_cert");

            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
                Quickstart.EnrollToNone with { ToCert = true });
            await AssertSameNameRejected(s, name, "01-add-failure-blurb");
            AssertSentEnrollMode(s, "cert");
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    // ZET's RequireOidcAuth needs no check here: the quickstart controller always serves OIDC.
    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesEnrollAuthPolicy()
    {
        string name = nameof(EnrollToCertUsesEnrollAuthPolicy);
        const string identityName = "test_ext_auth_enroll_auth_policy";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, AuthPolicy = TotpPolicy }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            await Dex.DriveIdPFlowAsync(JoinToEnrollmentUrl(s), $"{identityName}@test.com");
            // A partial auth sends no identity added event, so the identifier comes from the mfa event.
            JObject mfa = WaitForZetEventAfter(s, AddIdentityLine, EnrollmentRequiredEvent);
            await AssertRowNeedsMfaSetup(s, name);
            AssertJwtEnrolledIdentityFile((string)mfa["Identifier"]!);
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesEnrollAuthPolicy()
    {
        string name = nameof(EnrollToTokenUsesEnrollAuthPolicy);
        const string identityName = "test_ext_auth_token_enroll_auth_policy";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true, AuthPolicy = TotpPolicy }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            await Dex.DriveIdPFlowAsync(JoinToEnrollmentUrl(s), $"{identityName}@test.com");
            string identityFile = (string)WaitForNeedsExtLogin(s, AddIdentityLine)["Id"]!["Identifier"]!;
            await Dex.DriveIdPFlowAsync(LoginFromRow(s), $"{identityName}@test.com");
            WaitForZetEventAfter(s, ExternalAuthLine, EnrollmentRequiredEvent);
            await AssertRowNeedsMfaSetup(s, name);
            AssertUrlEnrolledToNoneIdentityFile(identityFile);
        });
    }

    /// <summary>
    /// ZET's status check after enrollment_required: the row asks to set up MFA only while the identity is NeedsExtAuth
    /// false, MfaNeeded true and MfaEnabled false.
    /// </summary>
    private static async Task AssertRowNeedsMfaSetup(AppiumSession s, string name)
    {
        WaitForController(s, By.XPath("//*[@AutomationId='MfaSetupNeeded']"), "the row asks to set up MFA");
        Assert.DoesNotContain(s.Driver.FindElements(ExtAuthRequiredIcon), e => e.Displayed);
        Assert.Equal(1, IdentityRowCount(s));
        await VerifyStep(Capture(s), name, "01-setup-needed-row");
    }

    /// <summary>ZET's completeEnrollToCert through the URL dialog. Returns the identity file.</summary>
    private static async Task<string> CompleteEnrollToCert(AppiumSession s, string identityName)
    {
        PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        return await FinishEnrollToCert(s, identityName, JoinToEnrollmentUrl(s));
    }

    /// <summary>ZET's completeEnrollToCert from ZET's AddIdentity reply on. Returns the identity file.</summary>
    private static async Task<string> FinishEnrollToCert(AppiumSession s, string identityName, string authUrl)
    {
        await Dex.DriveIdPFlowAsync(authUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(s, AddIdentityLine)["Id"]!["Identifier"]!;
        // ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-cert checks what the JWT one does.
        AssertJwtEnrolledIdentityFile(identityFile);
        WaitUntil(s, "the enrolled identity shows on the landing list", ControllerTimeout,
            () => IdentityRowCount(s) == 1);
        return identityFile;
    }

    /// <summary>ZET's completeEnrollToToken through the URL dialog, then the row's login. Returns the identity file.</summary>
    private static async Task<string> CompleteEnrollToToken(AppiumSession s, string identityName)
    {
        PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        return await FinishEnrollToToken(s, identityName, JoinToEnrollmentUrl(s));
    }

    /// <summary>
    /// ZET's completeEnrollToToken from ZET's AddIdentity reply on, then the row's login. Returns the identity file.
    /// </summary>
    private static async Task<string> FinishEnrollToToken(AppiumSession s, string identityName, string enrollUrl)
    {
        await Dex.DriveIdPFlowAsync(enrollUrl, $"{identityName}@test.com");
        WaitForNeedsExtLogin(s, AddIdentityLine);
        string loginUrl = LoginFromRow(s);
        await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
        string identityFile = (string)AssertEnrollmentAdded(s, ExternalAuthLine)["Id"]!["Identifier"]!;
        // ZET's AssertValidUrlEnrolledIdentityFile for enroll-to-token checks what the none one does.
        AssertUrlEnrolledToNoneIdentityFile(identityFile);
        WaitForRowLoggedIn(s);
        return identityFile;
    }

    private static void WaitForRowLoggedIn(AppiumSession s) =>
        WaitUntil(s, "the row no longer asks for external auth", ControllerTimeout,
            () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));

    // A Label's UIA Name is its whole Content, even when the row truncates it.
    private static void WaitForRowNamed(AppiumSession s, string identityName) =>
        WaitForController(s, By.XPath($"//*[@Name='{identityName}']"), $"the row is named {identityName}");
}
