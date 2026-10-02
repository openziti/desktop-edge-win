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
        ClickAt(s, WaitFor(s, MfaToggle));
        WaitForId(s, "AuthCode");
    }

    /// <summary>Enroll, then remove MFA from the identity's details with the code the enrollment picks.</summary>
    private async Task RemoveAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        OpenRemovalPrompt(s, identityName);
        await VerifyStep(Capture(s), name, "01-remove-code-prompt");
        // RemoveMFA only goes out once a code is submitted.
        Assert.Equal(0, UiCmdLineCount(s.Relay!, RemoveMfaLine));

        WaitForId(s, "AuthCode").SendKeys(pickCode(enrollment));
        SaveStep(s, name, "02-code-typed");
        JObject reply = SendAndWaitForZetReply(s, RemoveMfaLine, () => WaitForId(s, "AuthButton").Click());
        Assert.Equal(0, (int?)reply["Code"]);
        // The "MFA disabled" blurb shows on ZET's enrollment_remove event, not on the reply.
        AssertMfaEventSucceeded(s, RemoveMfaLine, "enrollment_remove");
        await VerifyStep(CaptureBlurbOnEvent(s, RemoveMfaLine, "\"Action\":\"enrollment_remove\""), name,
            "03-removed-details");
        WaitForGone(s, By.XPath("//*[@AutomationId='AuthCode']"));
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsValidTotp() =>
        await RemoveAccepts(nameof(RemoveAcceptsValidTotp), "test_mfa_remove_valid_totp",
            e => Totp.Compute(e.Secret, DateTimeOffset.UtcNow));

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsRecoveryCode() =>
        await RemoveAccepts(nameof(RemoveAcceptsRecoveryCode), "test_mfa_remove_recovery_code",
            e => e.RecoveryCodes[0]);

    [Fact(Timeout = 180000)]
    public async Task RemoveRejectsInvalidTotp()
    {
        string name = nameof(RemoveRejectsInvalidTotp);
        const string identityName = "test_mfa_remove_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        OpenRemovalPrompt(s, identityName);
        WaitForId(s, "AuthCode").SendKeys("000000");
        SaveStep(s, name, "01-code-typed");
        // MFAScreen shows "Authentication Failed" on the failed reply.
        JObject reply = SendAndWaitForZetReply(s, RemoveMfaLine, () => WaitForId(s, "AuthButton").Click());
        await VerifyStep(CaptureBlurbOnReply(s, RemoveMfaLine), name, "02-after-rejection");

        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        // MFAScreen keeps the prompt open and clears the code on a failed RemoveMFA reply.
        Assert.Equal("", WaitForId(s, "AuthCode").Text);
    }
}
