using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestMFARecoveryCodes in ziti-tunnel-sdk-c tests/integration/mfa_test.go. The getMfaCodes subtests have
/// no twin: IdentityDetails.UpdateView always collapses the recovery button, so the UI never sends GetMFACodes.
/// </summary>
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
        Trace.Begin();
        string name = nameof(GenerateMfaCodesReplacesOldSet);
        const string identityName = "test_mfa_regenerate_codes";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = EnrollMfaToRecoveryCodes(_fixture, s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//Text[@Name='Regenerate']")));
        WaitForId(s, "AuthCode");
        await Trace.Settle(350);
        SaveStep(s, name, "01-regenerate-prompt");
        await VerifyScreen(Capture(s), "regenerate-prompt", name);

        WaitForId(s, "AuthCode").SendKeys(Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        SaveStep(s, name, "02-code-typed");
        WaitForId(s, "AuthButton").Click();
        // MainWindow.DoClose reopens the codes dialog with the GenerateMFACodes reply's codes.
        WaitForController(s, By.XPath("//Text[@Name='MFA Recovery Codes']"), "the new recovery codes show");
        await Trace.Settle(350);
        byte[] regenerated = Capture(s);
        SaveStep(regenerated, name, "03-new-recovery-codes");
        await VerifyScreen(MaskedCentered(s, regenerated, RecoveryCodeBoxes, RecoveryCodeMaskWidth, RecoveryDialogBackground), "new-recovery-codes", name);
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"GenerateMFACodes\"");
        Assert.Equal(0, (int?)reply["Code"]);
        List<string> newCodes = ReadRecoveryCodes(s);
        Assert.NotEmpty(newCodes);
        Assert.Equal(reply["Data"]!["RecoveryCodes"]!.Values<string>().Select(code => code!).ToList(), newCodes);
        Assert.Empty(newCodes.Intersect(enrollment.RecoveryCodes));

        ClickUntilGone(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        CloseIdentityDetails(s);
        TriggerReauthChallenge(s, identityName);
        await AuthenticateFromRow(s, name, "04-new-code-typed", identityName, newCodes[0]);
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, "\"Command\":\"SubmitMFA\"")["Code"]);
        CloseIdentityDetails(s);

        TriggerReauthChallenge(s, identityName);
        byte[] rejected = await RejectFromRow(s, name, "05-old-code-typed", identityName, enrollment.RecoveryCodes[0]);
        SaveStep(rejected, name, "06-after-rejection");
        // The recovery code differs every run.
        await VerifyScreen(Masked(s, rejected, By.XPath("//*[@AutomationId='AuthCode']")), "after-rejection", name);
    }
}
