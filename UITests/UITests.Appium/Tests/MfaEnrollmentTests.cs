using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaEnrollmentTests
{
    private readonly IntegrationFixture _fixture;

    public MfaEnrollmentTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 180000)]
    public async Task EnrollCompletesWithTotpRequiredPolicy()
    {
        string name = nameof(EnrollCompletesWithTotpRequiredPolicy);
        const string identityName = "test_mfa_enable_totp_policy";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        WriteTestJwt(_fixture.Quickstart.GetJwtFromController(identityName));
        ClickAddIdentityWithJwt(session);
        WaitForController(session, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded"), "the row asks to set up MFA");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, AddIdentityLine)["Code"]);
        WaitForZetEventAfter(session, AddIdentityLine, MfaEnrollmentRequiredEvent);
        SortAscending(session, "Name");
        await VerifyStep(session, Capture(session), name, "01-setup-needed-row");

        ClickAt(session, WaitFor(session, InIdentityRow(AddedIdentityFileName, "MfaSetupNeeded")));
        WaitForController(session, ById("SetupCode"), "the MFA setup dialog opens");
        await VerifyStep(session, Masked(session, Capture(session), ById("MFAImage")), name, "02-mfa-qr-dialog");

        CompleteMfaSetup(session);
        await VerifyStep(session, MaskedCentered(session, Capture(session), RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground),
            name, "03-mfa-recovery-codes");

        ClickUntilGone(session, ById("CloseBlack"));
        WaitForController(session, By.XPath($"//Text[@Name='{identityName}']"), $"the row shows {identityName}");
        WaitForGone(session, InIdentityRow(identityName, "MfaSetupNeeded"));
        await VerifyStep(session, Capture(session), name, "04-enrolled-row");
        Assert.Empty(session.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollRejectsInvalidTotp()
    {
        string name = nameof(EnrollRejectsInvalidTotp);
        const string identityName = "test_mfa_verify_invalid_totp";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        WaitForZetEventAfter(session, AddIdentityLine, ControllerConnectedEvent);
        OpenMfaSetup(session, identityName);
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, EnableMfaLine)["Code"]);
        TypeUntil(session, ById("SetupCode"), "000000");
        SaveStep(session, name, "01-invalid-code-typed");
        JObject reply = SendAndWaitForZetReply(session, VerifyMfaLine, () => WaitForId(session, "AuthSetupButton").Click());

        const string verificationEvent = "\"Op\":\"mfa\",\"Action\":\"enrollment_verification\"";
        WaitForZetEventAfter(session, VerifyMfaLine, verificationEvent);
        await VerifyStep(session, CaptureBlurbOnEvent(session, VerifyMfaLine, verificationEvent), name, "02-after-rejection");
        Assert.Empty(session.Driver.FindElements(ById("SetupCode")));
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
    }
}
