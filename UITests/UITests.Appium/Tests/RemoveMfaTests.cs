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

    [Fact(Timeout = 180000)]
    public async Task RemoveAcceptsValidTotp()
    {
        Trace.Begin();
        string name = nameof(RemoveAcceptsValidTotp);
        const string identityName = "test_mfa_remove_valid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']")));
        WaitForId(s, "AuthCode");
        await Trace.Settle(350);
        SaveStep(s, name, "01-remove-code-prompt");
        await VerifyScreen(Capture(s), "remove-code-prompt", name);
        // RemoveMFA only goes out once a code is submitted.
        Assert.False(UiSent(s.Relay!, "RemoveMFA"));

        WaitForId(s, "AuthCode").SendKeys(Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));
        SaveStep(s, name, "02-code-typed");
        WaitForId(s, "AuthButton").Click();
        // IsMFAEnabled clears on ZET's enrollment_remove event, not on the RemoveMFA reply.
        WaitUntil(s, "ZET sends a successful enrollment_remove event", ControllerTimeout,
            () => s.Relay!.Recorded
                .Where(r => r.From == "zet" && r.Pipe == "event")
                .Select(r => JObject.Parse(r.Line))
                .Any(e => (string?)e["Action"] == "enrollment_remove" && (bool?)e["Successful"] == true));
        WaitForGone(s, By.XPath("//*[@AutomationId='AuthCode']"));
        await Trace.Settle(350);
        SaveStep(s, name, "03-removed-details");
        await VerifyScreen(Capture(s), "removed-details", name);
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"RemoveMFA\"");
        Assert.Equal(0, (int?)reply["Code"]);
    }
}
