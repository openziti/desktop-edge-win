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
public class MfaRecoveryCodesTests
{
    private readonly IntegrationFixture _fixture;

    public MfaRecoveryCodesTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 240000)]
    public async Task GenerateMfaCodesReplacesOldSet()
    {
        string name = nameof(GenerateMfaCodesReplacesOldSet);
        const string identityName = "test_mfa_regenerate_codes";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(_fixture, s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//Text[@Name='Regenerate']")));
        WaitForId(s, "AuthCode");
        await VerifyStep(Capture(s), name, "01-regenerate-prompt");

        TypeUntil(s, ById("AuthCode"), Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        SaveStep(s, name, "02-code-typed");
        JObject reply = SendAndWaitForZetReply(s, "\"Command\":\"GenerateMFACodes\"",
            () => WaitForId(s, "AuthButton").Click());
        // MainWindow.DoClose reopens the codes dialog with the GenerateMFACodes reply's codes.
        Assert.Equal(0, (int?)reply["Code"]);
        List<string> newCodes = reply["Data"]!["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList();
        WaitForRecoveryCodes(s, newCodes);
        Assert.Empty(newCodes.Intersect(enrollment.RecoveryCodes));
        await VerifyStep(MaskedCentered(s, Capture(s), RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground),
            name, "03-new-recovery-codes");

        ClickUntilGone(s, ById("CloseBlack"));
        CloseIdentityDetails(s);
        TriggerReauthChallenge(s, identityName);
        AuthenticateFromRow(s, name, "04-new-code-typed", identityName, newCodes[0]);
        CloseIdentityDetails(s);

        TriggerReauthChallenge(s, identityName);
        byte[] rejected = RejectFromRow(s, name, "05-old-code-typed", identityName, enrollment.RecoveryCodes[0]);
        // The recovery code differs every run.
        await VerifyStep(Masked(s, rejected, ById("AuthCode")), name, "06-after-rejection");
    }
}
