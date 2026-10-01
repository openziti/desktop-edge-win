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
    private readonly IntegrationFixture _fixture;

    public ExternalAuthSingleSignerTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneCompletes()
    {
        Trace.Begin();
        string name = nameof(EnrollToNoneCompletes);
        const string identityName = "test_ext_auth_none_happy";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            JoinEnrolledToNone(s);
            // The working signer enrolls to neither, so the app sends no mode.
            JObject sent = UiCommand(s.Relay!, AddIdentityLine);
            Assert.Null((string?)sent["Data"]!["EnrollMode"]);
            WaitForController(s, ExtAuthRequiredIcon, "the row asks for external auth");
            await Trace.Settle(350);
            SaveStep(s, name, "01-identity-needs-ext-login");
            await VerifyScreen(Capture(s), "identity-needs-ext-login");

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
            AssertUrlEnrolledToNoneIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitForRowLoggedIn(s);
            await Trace.Settle(350);
            SaveStep(s, name, "02-identity-enrolled");
            await VerifyScreen(Capture(s), "identity-enrolled");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneRejectsUnknownControllerIdentity()
    {
        Trace.Begin();
        string name = nameof(EnrollToNoneRejectsUnknownControllerIdentity);
        // The fixture has no controller identity with this external id, so dex accepts the login and the controller
        // rejects it.
        const string identityName = "test_ext_auth_unknown_identity";

        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            JoinEnrolledToNone(s);

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            WaitForZetEventAfter(s, ExternalAuthLine, "\"Op\":\"controller\",\"Action\":\"disconnected\"");
            await Trace.Settle(350);
            SaveStep(s, name, "01-login-rejected");
            await VerifyScreen(Capture(s), "login-rejected");
        }
        finally
        {
            CloseBrowsers();
        }
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertCompletes()
    {
        Trace.Begin();
        string name = nameof(EnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await CompleteEnrollToCert(s, "test_ext_auth_cert_happy");
            // One cert-only signer, so the app picks it and sends no provider.
            Assert.Equal("cert", (string?)UiCommand(s.Relay!, AddIdentityLine)["Data"]!["EnrollMode"]);
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertUsesNameClaimSelector()
    {
        Trace.Begin();
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
        Trace.Begin();
        string name = nameof(EnrollToTokenCompletes);
        const string identityName = "test_ext_auth_token_happy";
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            string enrollUrl = JoinToEnrollmentUrl(s);
            Assert.Equal("token", (string?)UiCommand(s.Relay!, AddIdentityLine)["Data"]!["EnrollMode"]);

            await Dex.DriveIdPFlowAsync(enrollUrl, $"{identityName}@test.com");
            WaitForNeedsExtLogin(s, AddIdentityLine);
            await Trace.Settle(350);
            SaveStep(s, name, "01-identity-needs-ext-login");
            await VerifyScreen(Capture(s), "identity-needs-ext-login");

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
            AssertUrlEnrolledToTokenIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitForRowLoggedIn(s);
        });
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenUsesNameClaimSelector()
    {
        Trace.Begin();
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
        Trace.Begin();
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
        Trace.Begin();
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
        Trace.Begin();
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
        Trace.Begin();
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
        Trace.Begin();
        string name = nameof(BothEnabledEnrollToCertCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await OpenEnrollChoice(s);
            ChooseDeviceCertificate(s);
            await Trace.Settle(350);
            SaveStep(s, name, "01-enroll-choice-device-certificate");
            await VerifyScreen(Capture(s), "enroll-choice-device-certificate");
            await FinishEnrollToCert(s, "test_ext_auth_cert_both", JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "cert");
        });
    }

    [Fact(Timeout = 150000)]
    public async Task BothEnabledEnrollToTokenCompletes()
    {
        Trace.Begin();
        string name = nameof(BothEnabledEnrollToTokenCompletes);
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await OpenEnrollChoice(s);
            Assert.True(WaitFor(s, UserSessionRadio).Selected, "User session is not the default enrollment");
            Assert.False(WaitFor(s, DeviceCertificateRadio).Selected, "Device certificate is selected by default");
            // One signer, so there is no provider to pick.
            Assert.Empty(s.Driver.FindElements(SignerPickerLabel));
            await Trace.Settle(350);
            SaveStep(s, name, "01-enroll-choice");
            await VerifyScreen(Capture(s), "enroll-choice");

            await FinishEnrollToToken(s, "test_ext_auth_token_both", JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "token");
        });
    }

    /// <summary>ZET's completeEnrollToCert through the URL dialog. Returns ZET's identity added event.</summary>
    private static async Task<JObject> CompleteEnrollToCert(AppiumSession s, string identityName)
    {
        await PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        return await FinishEnrollToCert(s, identityName, JoinToEnrollmentUrl(s));
    }

    /// <summary>ZET's completeEnrollToCert from ZET's AddIdentity reply on. Returns ZET's identity added event.</summary>
    private static async Task<JObject> FinishEnrollToCert(AppiumSession s, string identityName, string authUrl)
    {
        await Dex.DriveIdPFlowAsync(authUrl, $"{identityName}@test.com");
        JObject added = AssertEnrollmentAdded(s, AddIdentityLine);
        AssertUrlEnrolledToCertIdentityFile((string)added["Id"]!["Identifier"]!);
        WaitUntil(s, "the enrolled identity shows on the landing list", ControllerTimeout,
            () => IdentityRowCount(s) == 1);
        return added;
    }

    /// <summary>
    /// ZET's completeEnrollToToken through the URL dialog, then the row's login. Returns ZET's identity added event.
    /// </summary>
    private static async Task<JObject> CompleteEnrollToToken(AppiumSession s, string identityName)
    {
        await PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        return await FinishEnrollToToken(s, identityName, JoinToEnrollmentUrl(s));
    }

    /// <summary>
    /// ZET's completeEnrollToToken from ZET's AddIdentity reply on, then the row's login. Returns ZET's identity added
    /// event.
    /// </summary>
    private static async Task<JObject> FinishEnrollToToken(AppiumSession s, string identityName, string enrollUrl)
    {
        await Dex.DriveIdPFlowAsync(enrollUrl, $"{identityName}@test.com");
        WaitForNeedsExtLogin(s, AddIdentityLine);
        string loginUrl = LoginFromRow(s);
        await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
        JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
        AssertUrlEnrolledToTokenIdentityFile((string)added["Id"]!["Identifier"]!);
        WaitForRowLoggedIn(s);
        return added;
    }

    private static void WaitForRowLoggedIn(AppiumSession s) =>
        WaitUntil(s, "the row no longer asks for external auth", ControllerTimeout,
            () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));

    // A Label's UIA Name is its whole Content, even when the row truncates it.
    private static void WaitForRowNamed(AppiumSession s, string identityName) =>
        WaitForController(s, By.XPath($"//*[@Name='{identityName}']"), $"the row is named {identityName}");
}
