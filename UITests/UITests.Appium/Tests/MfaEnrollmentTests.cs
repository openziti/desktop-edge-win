using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of TestMFAEnrollment in ziti-tunnel-sdk-c tests/integration/mfa_test.go.</summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaEnrollmentTests
{
    private readonly IntegrationFixture _fixture;

    public MfaEnrollmentTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 180000)]
    public async Task EnrollCompletesWithTotpRequiredPolicy()
    {
        Trace.Begin();
        string name = nameof(EnrollCompletesWithTotpRequiredPolicy);
        const string identityName = "test_mfa_enable_totp_policy";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        ClickAddIdentityWithJwt(s);
        // Partially authenticated until TOTP is enrolled, so ZET sends no added event with the controller name and the
        // row keeps the file name. The prompt shows on ZET's enrollment_required event.
        WaitForController(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, AddIdentityLine)["Code"]);
        WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"mfa\",\"Action\":\"enrollment_required\"");
        SortByNameAscending(s);
        await Trace.Settle(350);
        SaveStep(s, name, "01-setup-needed-row");
        await VerifyScreen(Capture(s), "setup-needed-row");

        // The QR dialog opens on ZET's enrollment_challenge event, not on the EnableMFA reply.
        ClickAt(s, WaitFor(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded")));
        WaitForController(s, By.XPath("//*[@AutomationId='SetupCode']"), "the MFA setup dialog opens");
        await Trace.Settle(600); // QR render
        SaveStep(s, name, "02-mfa-qr-dialog");
        await VerifyScreen(Masked(s, Capture(s), By.XPath("//*[@AutomationId='MFAImage']")), "mfa-qr-dialog");

        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        WaitForId(s, "SetupCode").SendKeys(Totp.Compute(secret, DateTimeOffset.UtcNow));
        SaveStep(s, name, "03-totp-typed");
        WaitForId(s, "AuthSetupButton").Click();

        // A verified code swaps the setup dialog for the recovery codes, on ZET's enrollment_verification event.
        WaitForController(s, By.XPath("//Text[@Name='MFA Recovery Codes']"), "the recovery codes show");
        await Trace.Settle(300);
        SaveStep(s, name, "04-mfa-recovery-codes");
        await VerifyScreen(MaskedCentered(s, Capture(s), RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground), "mfa-recovery-codes");
        AssertMfaEnrollmentVerified(s);
        // The baseline masks the codes, so their text is checked against the enrollment_challenge event the app took
        // them from.
        JObject challenge = AssertMfaEventSucceeded(s, EnableMfaLine, "enrollment_challenge");
        List<string> sent = challenge["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList();
        Assert.NotEmpty(sent);
        Assert.Equal(sent, ReadRecoveryCodes(s));

        ClickUntilGone(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        // Fully authenticated now, so ZET's added event renames the row to the controller name.
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"the row shows {identityName}");
        WaitForGone(s, InIdentityRow(identityName, "MfaSetupNeeded"));
        await Trace.Settle(350);
        SaveStep(s, name, "05-enrolled-row");
        await VerifyScreen(Capture(s), "enrolled-row");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollRejectsInvalidTotp()
    {
        Trace.Begin();
        string name = nameof(EnrollRejectsInvalidTotp);
        const string identityName = "test_mfa_verify_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        WaitForZetEventAfter(s, AddIdentityLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']")));
        WaitForController(s, By.XPath("//*[@AutomationId='SetupCode']"), "the MFA setup dialog opens");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, EnableMfaLine)["Code"]);
        WaitForId(s, "SetupCode").SendKeys("000000");
        SaveStep(s, name, "01-invalid-code-typed");
        WaitForId(s, "AuthSetupButton").Click();

        // MFAScreen closes the setup dialog when VerifyMFA fails, and ZET's failed enrollment_verification event
        // raises the blurb. The capture is timed from the event, since finding the blurb through UIA can take most of
        // its 2.5s.
        WaitForZetEventAfter(s, VerifyMfaLine, "\"Op\":\"mfa\",\"Action\":\"enrollment_verification\"");
        byte[] rejected = Capture(s);
        SaveStep(rejected, name, "02-after-rejection");
        await VerifyScreen(rejected, "after-rejection");
        Assert.Empty(s.Driver.FindElements(By.XPath("//*[@AutomationId='SetupCode']")));
        JObject reply = ZetReplyTo(s.Relay!, VerifyMfaLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
    }
}
