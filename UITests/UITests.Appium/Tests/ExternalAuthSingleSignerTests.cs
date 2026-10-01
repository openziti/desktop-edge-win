using Newtonsoft.Json.Linq;
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
    public async Task EnrollToCertCompletes()
    {
        Trace.Begin();
        string name = nameof(EnrollToCertCompletes);
        const string identityName = "test_ext_auth_cert_happy";

        _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
            Quickstart.EnrollToNone with { ToCert = true });
        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            string authUrl = JoinToEnrollmentUrl(s);
            // One cert-only signer, so the app picks it and sends no provider.
            JObject sent = UiCommand(s.Relay!, AddIdentityLine);
            Assert.Equal("cert", (string?)sent["Data"]!["EnrollMode"]);

            await Dex.DriveIdPFlowAsync(authUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, AddIdentityLine);
            AssertUrlEnrolledToCertIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitUntil(s, "the enrolled identity shows on the landing list", ControllerTimeout,
                () => IdentityRowCount(s) == 1);
            await Trace.Settle(350);
            SaveStep(s, name, "01-identity-enrolled");
            await VerifyScreen(Capture(s), "identity-enrolled");
        }
        finally
        {
            CloseBrowsers();
            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, Quickstart.EnrollToNone);
        }
    }

    [Fact(Timeout = 150000)]
    public async Task EnrollToTokenCompletes()
    {
        Trace.Begin();
        string name = nameof(EnrollToTokenCompletes);
        const string identityName = "test_ext_auth_token_happy";

        _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName,
            Quickstart.EnrollToNone with { ToToken = true });
        try
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            await PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            string enrollUrl = JoinToEnrollmentUrl(s);
            JObject sent = UiCommand(s.Relay!, AddIdentityLine);
            Assert.Equal("token", (string?)sent["Data"]!["EnrollMode"]);

            await Dex.DriveIdPFlowAsync(enrollUrl, $"{identityName}@test.com");
            WaitForNeedsExtLogin(s, AddIdentityLine);
            await Trace.Settle(350);
            SaveStep(s, name, "01-identity-needs-ext-login");
            await VerifyScreen(Capture(s), "identity-needs-ext-login");

            string loginUrl = LoginFromRow(s);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
            AssertUrlEnrolledToTokenIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitUntil(s, "the row no longer asks for external auth", ControllerTimeout,
                () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));
            await Trace.Settle(350);
            SaveStep(s, name, "02-identity-enrolled");
            await VerifyScreen(Capture(s), "identity-enrolled");
        }
        finally
        {
            CloseBrowsers();
            _fixture.Quickstart.UpdateExtJwtSigner(IntegrationFixture.WorkingSignerName, Quickstart.EnrollToNone);
        }
    }
}
