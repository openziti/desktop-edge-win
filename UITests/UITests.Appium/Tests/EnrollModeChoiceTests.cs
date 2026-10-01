using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Which enrollment the URL dialog offers for the controller's one ext-jwt signer, and which EnrollMode it sends. The
/// choice between a user session (token) and a device certificate (cert) belongs only to a signer with both enabled
/// (openziti/desktop-edge-win#1092). No ZET test has a twin here: the choice is the UI's. Each test denies the IdP
/// login it starts, because the ExternalAuthSingleSignerTests twins finish both enrollments.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class EnrollModeChoiceTests
{
    // A RadioButton's UIA Name is empty when its content is a panel, so each is found by its label's TextBlock.
    private static readonly By UserSessionRadio = By.XPath("//RadioButton[.//*[@Name='User session']]");
    private static readonly By DeviceCertificateRadio = By.XPath("//RadioButton[.//*[@Name='Device certificate']]");
    private static readonly By SignerPickerLabel = By.XPath("//*[@Name='Identity Provider']");

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

    [Fact(Timeout = 90000)]
    public async Task CertAndTokenSignerDefaultsToUserSession()
    {
        Trace.Begin();
        string name = nameof(CertAndTokenSignerDefaultsToUserSession);
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

            await DenyEnrollment(s, JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "token");
        });
    }

    [Fact(Timeout = 90000)]
    public async Task CertAndTokenSignerSendsDeviceCertificateChoice()
    {
        Trace.Begin();
        await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true, ToToken = true }, async () =>
        {
            await using AppiumSession s =
                await LaunchAsync(_fixture, nameof(CertAndTokenSignerSendsDeviceCertificateChoice));
            await OpenEnrollChoice(s);
            IWebElement deviceCertificate = WaitFor(s, DeviceCertificateRadio);
            ClickAt(s, deviceCertificate);
            WaitUntil(s, "Device certificate is selected", TimeSpan.FromSeconds(5), () => deviceCertificate.Selected);
            Assert.False(WaitFor(s, UserSessionRadio).Selected, "User session stayed selected");

            await DenyEnrollment(s, JoinFromEnrollChoice(s));
            AssertSentEnrollMode(s, "cert");
        });
    }

    private static async Task OpenEnrollChoice(AppiumSession s)
    {
        await PrepareTestWindow(s);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        Assert.True(JoinOpensEnrollChoice(s), "the app sent AddIdentity without offering the enrollment choice");
    }

    // One capable signer, so the app picks it and sends no provider.
    private static void AssertSentEnrollMode(AppiumSession s, string expected)
    {
        JObject sent = UiCommand(s.Relay!, AddIdentityLine);
        Assert.Equal(expected, (string?)sent["Data"]!["EnrollMode"]);
        Assert.Null((string?)sent["Data"]!["Provider"]);
    }
}
