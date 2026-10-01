using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of TestRemoveMFA in ziti-tunnel-sdk-c tests/integration/mfa_test.go.</summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class RemoveMfaTests
{
    private readonly IntegrationFixture _fixture;

    public RemoveMfaTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Turn MFA off from the identity's details, which opens the prompt for the removal code.</summary>
    private static void OpenRemovalPrompt(AppiumSession s, string identityName)
    {
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']")));
        WaitForId(s, "AuthCode");
    }

    /// <summary>Enroll, then remove MFA from the identity's details with the code the enrollment picks.</summary>
    private async Task RemoveAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        OpenRemovalPrompt(s, identityName);
        await Trace.Settle(350);
        SaveStep(s, name, "01-remove-code-prompt");
        await VerifyScreen(Capture(s), "remove-code-prompt", name);
        // RemoveMFA only goes out once a code is submitted.
        Assert.False(UiSent(s.Relay!, "RemoveMFA"));

        WaitForId(s, "AuthCode").SendKeys(pickCode(enrollment));
        SaveStep(s, name, "02-code-typed");
        WaitForId(s, "AuthButton").Click();
        WaitForBlurb(s, "MFA disabled, access may be limited");
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so this capture comes before the slower checks.
        byte[] removed = Capture(s);
        SaveStep(removed, name, "03-removed-details");
        await VerifyScreen(removed, "removed-details", name);
        // IsMFAEnabled clears on ZET's enrollment_remove event, not on the RemoveMFA reply.
        AssertMfaEventSucceeded(s, "\"Command\":\"RemoveMFA\"", "enrollment_remove");
        WaitForGone(s, By.XPath("//*[@AutomationId='AuthCode']"));
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"RemoveMFA\"");
        Assert.Equal(0, (int?)reply["Code"]);
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsValidTotp()
    {
        Trace.Begin();
        await RemoveAccepts(nameof(RemoveAcceptsValidTotp), "test_mfa_remove_valid_totp",
            e => Totp.Compute(e.Secret, DateTimeOffset.UtcNow));
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsRecoveryCode()
    {
        Trace.Begin();
        await RemoveAccepts(nameof(RemoveAcceptsRecoveryCode), "test_mfa_remove_recovery_code",
            e => e.RecoveryCodes[0]);
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveRejectsInvalidTotp()
    {
        Trace.Begin();
        string name = nameof(RemoveRejectsInvalidTotp);
        const string identityName = "test_mfa_remove_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        OpenRemovalPrompt(s, identityName);
        WaitForId(s, "AuthCode").SendKeys("000000");
        SaveStep(s, name, "01-code-typed");
        WaitForId(s, "AuthButton").Click();
        WaitForBlurb(s, "Authentication Failed");
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so this capture comes before the slower asserts.
        byte[] rejected = Capture(s);
        SaveStep(rejected, name, "02-after-rejection");
        await VerifyScreen(rejected, "after-rejection", name);

        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"RemoveMFA\"");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        // MFAScreen keeps the prompt open and clears the code on a failed RemoveMFA reply.
        Assert.Equal("", WaitForId(s, "AuthCode").Text);
    }
}
