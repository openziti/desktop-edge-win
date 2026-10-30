using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class RemoveMfaTests
{
    private readonly IntegrationFixture _fixture;

    public RemoveMfaTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    private async Task RemoveAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);
        OpenRemovalPrompt(session, identityName);
        await VerifyStep(session, Capture(session), name, "01-remove-code-prompt");
        Assert.Equal(0, UiCmdLineCount(session.Relay!, RemoveMfaLine));

        TypeUntil(session, ById("AuthCode"), pickCode(enrollment));
        SaveStep(session, name, "02-code-typed");
        JObject reply = SendAndWaitForZetReply(session, RemoveMfaLine, () => WaitForId(session, "AuthButton").Click());
        Assert.Equal(0, (int?)reply["Code"]);
        AssertMfaEventSucceeded(session, RemoveMfaLine, "enrollment_remove");
        await VerifyStep(session, CaptureBlurbOnEvent(session, RemoveMfaLine, "\"Action\":\"enrollment_remove\""), name,
            "03-removed-details");
        WaitForGone(session, ById("AuthCode"));
    }

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsValidTotp() =>
        await RemoveAccepts(nameof(RemoveAcceptsValidTotp), "test_mfa_remove_valid_totp",
            enrollment => Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsRecoveryCode() =>
        await RemoveAccepts(nameof(RemoveAcceptsRecoveryCode), "test_mfa_remove_recovery_code",
            enrollment => enrollment.RecoveryCodes[0]);

    [Fact(Timeout = 180000)]
    public async Task RemoveRejectsInvalidTotp()
    {
        string name = nameof(RemoveRejectsInvalidTotp);
        const string identityName = "test_mfa_remove_invalid_totp";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, session, identityName);
        OpenRemovalPrompt(session, identityName);
        TypeUntil(session, ById("AuthCode"), "000000");
        SaveStep(session, name, "01-code-typed");
        JObject reply = SendAndWaitForZetReply(session, RemoveMfaLine, () => WaitForId(session, "AuthButton").Click());
        await VerifyStep(session, CaptureBlurbOnReply(session, RemoveMfaLine), name, "02-after-rejection");

        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.Equal("", WaitForId(session, "AuthCode").Text);
    }
}
