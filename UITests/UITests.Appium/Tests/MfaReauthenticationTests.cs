using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of TestMFAReauthentication in ziti-tunnel-sdk-c tests/integration/mfa_test.go.</summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaReauthenticationTests
{
    private readonly IntegrationFixture _fixture;

    public MfaReauthenticationTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>ZET's triggerReauthChallenge from the row: off and back on, until the row asks to authenticate.</summary>
    private static void TriggerReauthChallenge(AppiumSession s, string identityName)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        // Shows on ZET's auth_challenge event.
        WaitForController(s, InIdentityRow(identityName, "MfaRequired"), "the row asks to authenticate");
    }

    /// <summary>Submit code from the row's MFA prompt, saving the typed code as step.</summary>
    private static void SubmitFromRow(AppiumSession s, string name, string step, string identityName, string code)
    {
        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "MfaRequired")));
        WaitForId(s, "AuthCode").SendKeys(code);
        SaveStep(s, name, step);
        WaitForId(s, "AuthButton").Click();
    }

    /// <summary>Submit code from the row's MFA prompt and wait for ZET to clear the lock.</summary>
    private static async Task AuthenticateFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        SubmitFromRow(s, name, step, identityName, code);
        // Lets the SubmitMFA reply and ZET's mfa_auth_status event land before UIA polling loads the UI thread.
        await Task.Delay(1000);

        // Clears on ZET's mfa_auth_status event.
        WaitUntil(s, "the row stops asking to authenticate", ControllerTimeout,
            () => s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")).Count == 0);
    }

    /// <summary>
    /// Submit code from the row's MFA prompt, assert ZET rejects it and the prompt stays open, and return the
    /// capture taken while the failure blurb shows.
    /// </summary>
    private static async Task<byte[]> RejectFromRow(AppiumSession s, string name, string step, string identityName,
        string code)
    {
        SubmitFromRow(s, name, step, identityName, code);
        // MFAScreen keeps the prompt open on a failed SubmitMFA reply.
        WaitForController(s, By.XPath("//*[@AutomationId='Blurb' and @Name='Authentication Failed']"),
            "the prompt says authentication failed");
        await Trace.Settle(350);
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so this capture comes before the slower asserts.
        byte[] png = Capture(s);
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"SubmitMFA\"");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("the token provided was invalid", (string?)reply["Error"]);
        Assert.NotEmpty(s.Driver.FindElements(By.XPath("//*[@AutomationId='AuthCode']")));
        Assert.NotEmpty(s.Driver.FindElements(InIdentityRow(identityName, "MfaRequired")));
        return png;
    }

    /// <summary>Enroll, trigger the reauth challenge, then authenticate with the code the enrollment picks.</summary>
    private async Task ReauthAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        TriggerReauthChallenge(s, identityName);
        await Trace.Settle(350);
        SaveStep(s, name, "01-authenticate-row");
        await VerifyScreen(Capture(s), "authenticate-row", name);

        await AuthenticateFromRow(s, name, "02-code-typed", identityName, pickCode(enrollment));
        await Trace.Settle(350);
        // Clicking the row's lock also opened the details, which the prompt closes back to.
        SaveStep(s, name, "03-authenticated-details");
        await VerifyScreen(Capture(s), "authenticated-details", name);
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"SubmitMFA\"");
        Assert.Equal(0, (int?)reply["Code"]);
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsValidTotp()
    {
        Trace.Begin();
        await ReauthAccepts(nameof(ReauthAcceptsValidTotp), "test_mfa_reauth_valid_totp",
            e => Totp.Compute(e.Secret, DateTimeOffset.UtcNow));
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsRecoveryCode()
    {
        Trace.Begin();
        await ReauthAccepts(nameof(ReauthAcceptsRecoveryCode), "test_mfa_reauth_recovery_code",
            e => e.RecoveryCodes[0]);
    }

    [Fact(Timeout = 240000)]
    public async Task ReauthRejectsRecoveryCodeReuse()
    {
        Trace.Begin();
        string name = nameof(ReauthRejectsRecoveryCodeReuse);
        const string identityName = "test_mfa_reauth_reused_recovery_code";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        string recoveryCode = AddIdentityAndEnrollMfa(_fixture, s, identityName).RecoveryCodes[0];
        TriggerReauthChallenge(s, identityName);
        await AuthenticateFromRow(s, name, "01-code-typed", identityName, recoveryCode);
        CloseIdentityDetails(s);

        TriggerReauthChallenge(s, identityName);
        byte[] rejected = await RejectFromRow(s, name, "02-code-reused", identityName, recoveryCode);
        SaveStep(rejected, name, "03-after-rejection");
        // The recovery code differs every run.
        await VerifyScreen(Masked(s, rejected, By.XPath("//*[@AutomationId='AuthCode']")), "after-rejection", name);
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthRejectsInvalidTotp()
    {
        Trace.Begin();
        string name = nameof(ReauthRejectsInvalidTotp);
        const string identityName = "test_mfa_reauth_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        TriggerReauthChallenge(s, identityName);
        byte[] rejected = await RejectFromRow(s, name, "01-code-typed", identityName, "000000");
        SaveStep(rejected, name, "02-after-rejection");
        await VerifyScreen(rejected, "after-rejection", name);
    }
}
