using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaRecoveryCodesTests
{
    private readonly IntegrationFixture _fixture;

    public MfaRecoveryCodesTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 240000)]
    public async Task GenerateMfaCodesReplacesOldSet()
    {
        string name = nameof(GenerateMfaCodesReplacesOldSet);
        const string identityName = "test_mfa_regenerate_codes";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(_fixture, session, identityName);
        ClickAt(session, WaitFor(session, By.XPath("//Text[@Name='Regenerate']")));
        WaitForId(session, "AuthCode");
        await VerifyStep(session, Capture(session), name, "01-regenerate-prompt");

        TypeUntil(session, ById("AuthCode"), Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        SaveStep(session, name, "02-code-typed");
        JObject reply = SendAndWaitForZetReply(session, "\"Command\":\"GenerateMFACodes\"",
            () => WaitForId(session, "AuthButton").Click());
        Assert.Equal(0, (int?)reply["Code"]);
        List<string> newCodes = reply["Data"]!["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList();
        // MainWindow.DoClose reopens the codes dialog with the GenerateMFACodes reply's codes.
        WaitForRecoveryCodes(session, newCodes);
        Assert.Empty(newCodes.Intersect(enrollment.RecoveryCodes));
        await VerifyStep(session, MaskedCentered(session, Capture(session), RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground),
            name, "03-new-recovery-codes");

        ClickUntilGone(session, ById("CloseBlack"));
        CloseIdentityDetails(session);
        TriggerReauthChallenge(session, identityName);
        AuthenticateFromRow(session, name, "04-new-code-typed", identityName, newCodes[0]);
        CloseIdentityDetails(session);

        TriggerReauthChallenge(session, identityName);
        byte[] rejected = RejectFromRow(session, name, "05-old-code-typed", identityName, enrollment.RecoveryCodes[0]);
        // The recovery code differs every run.
        await VerifyStep(session, Masked(session, rejected, ById("AuthCode")), name, "06-after-rejection");
    }
}
