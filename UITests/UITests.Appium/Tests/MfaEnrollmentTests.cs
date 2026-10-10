using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

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
        string name = nameof(EnrollCompletesWithTotpRequiredPolicy);
        const string identityName = "test_mfa_enable_totp_policy";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        ClickAddIdentityWithJwt(s);
        // Partially authenticated until TOTP is enrolled, so ZET sends no added event with the controller name and the
        // row keeps the file name. The prompt shows on ZET's enrollment_required event.
        WaitForController(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, AddIdentityLine)["Code"]);
        WaitForZetEventAfter(s, AddIdentityLine, MfaEnrollmentRequiredEvent);
        SortByNameAscending(s);
        await VerifyStep(Capture(s), name, "01-setup-needed-row");

        // The QR dialog opens on ZET's enrollment_challenge event, not on the EnableMFA reply.
        ClickAt(s, WaitFor(s, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded")));
        WaitForController(s, ById("SetupCode"), "the MFA setup dialog opens");
        await VerifyStep(Masked(s, Capture(s), ById("MFAImage")), name, "02-mfa-qr-dialog");

        ClickAt(s, WaitForId(s, "SecretButton"));
        string secret = WaitForId(s, "SecretCode").Text;
        TypeUntil(s, ById("SetupCode"), Totp.Compute(secret, DateTimeOffset.UtcNow));
        SendAndWaitForZetReply(s, VerifyMfaLine, () => WaitForId(s, "AuthSetupButton").Click());

        // A verified code swaps the setup dialog for the recovery codes, on ZET's enrollment_verification event.
        // The baseline masks the codes, so their text is checked against the enrollment_challenge event the app took
        // them from.
        WaitForRecoveryCodes(s, ChallengeRecoveryCodes(AssertMfaEnrollmentVerified(s)));
        await VerifyStep(MaskedCentered(s, Capture(s), RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground),
            name, "03-mfa-recovery-codes");

        ClickUntilGone(s, ById("CloseBlack"));
        // Fully authenticated now, so ZET's added event renames the row to the controller name.
        WaitForController(s, By.XPath($"//Text[@Name='{identityName}']"), $"the row shows {identityName}");
        WaitForGone(s, InIdentityRow(identityName, "MfaSetupNeeded"));
        await VerifyStep(Capture(s), name, "04-enrolled-row");
        Assert.Empty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollRejectsInvalidTotp()
    {
        string name = nameof(EnrollRejectsInvalidTotp);
        const string identityName = "test_mfa_verify_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        WaitForZetEventAfter(s, AddIdentityLine, ControllerConnectedEvent);
        OpenMfaSetup(s, identityName);
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, EnableMfaLine)["Code"]);
        TypeUntil(s, ById("SetupCode"), "000000");
        SaveStep(s, name, "01-invalid-code-typed");
        JObject reply = SendAndWaitForZetReply(s, VerifyMfaLine, () => WaitForId(s, "AuthSetupButton").Click());

        // MFAScreen closes the setup dialog when VerifyMFA fails, and ZET's failed enrollment_verification event
        // raises the blurb.
        const string verificationEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_verification\"";
        WaitForZetEventAfter(s, VerifyMfaLine, verificationEvent);
        await VerifyStep(CaptureBlurbOnEvent(s, VerifyMfaLine, verificationEvent), name, "02-after-rejection");
        Assert.Empty(s.Driver.FindElements(ById("SetupCode")));
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
    }
}
