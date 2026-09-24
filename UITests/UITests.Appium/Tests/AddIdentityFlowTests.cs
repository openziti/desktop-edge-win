using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One long flow from zero identities: add by JWT (one failure, two successes), enable MFA with a rejected code and
/// then a real TOTP code, regenerate recovery codes, disable and re-enable an identity, and forget it.
/// Under ZDEW_UI_TEST the app reads the JWT from %TEMP%\zdew-test-add-identity.jwt instead of opening a file dialog,
/// so each add writes a fake JWT there first.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "AddIdentityFlow")]
public class AddIdentityFlowTests
{
    private static string TestJwtPath =>
        Path.Combine(Path.GetTempPath(), "zdew-test-add-identity.jwt");

    /// <summary>
    /// A JWT carrying every field the app reads: `em` picks the enrollment path ("ott" sends AddIdentity), and a
    /// DEBUG-only Console.WriteLine throws when iss, sub, jti or aud is null.
    /// </summary>
    private static string FakeJwt()
    {
        string payload = "{\"iss\":\"mock\",\"sub\":\"mock\",\"jti\":\"mock\",\"aud\":[\"mock\"],\"em\":\"ott\"}";
        string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        return $"header.{b64}.signature";
    }

    private static void WriteFakeJwt() => File.WriteAllText(TestJwtPath, FakeJwt());

    private const string ByJwt2Identifier = "c:\\fake\\ids\\by-jwt-2.json";

    /// <summary>Click "Add Identity", then "With JWT" in its context menu.</summary>
    private static void ClickAddIdentityWithJwt(AppiumSession s)
    {
        // AddIdAreaButton has no UIA peer. Its "ADD" label does, and the MouseLeftButtonUp bubbles up to it.
        IWebElement addText = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, addText);

        IWebElement withJwt = WaitFor(s, By.XPath("//*[@Name='With JWT']"));
        ClickAt(s, withJwt);
    }

    private static int IdentityRowCount(AppiumSession s) =>
        s.Driver.FindElements(By.XPath("//Custom[@ClassName='IdentityItem']")).Count;

    // 20 steps with a UIA tree walk and screenshot each: a passing run takes about 60s.
    [Fact(Timeout = 180000)]
    [Trait("Category", "AddIdentityFlow")]
    [Trait("Category", "Screenshots")]
    public async Task FullIdentityLifecycle()
    {
        Trace.Begin();
        string name = nameof(FullIdentityLifecycle);

        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("no-identities.json"));
        WaitForId(s, "ConnectLabel");
        DismissWelcome(s);

        await PrepareTestWindow(s);
        SaveStep(s, name, "01-window-prepared");

        Assert.Equal(0, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Add by JWT, the mock replies with a failure.
        // -----------------------------------------------------------------
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentityFailure("simulated mock-side enrollment failure");
        ClickAddIdentityWithJwt(s);

        // The app shows the failure as a blurb.
        JObject addRequest = WaitForCommand(s, "AddIdentity", 0);
        await Trace.Settle(500); // let blurb render
        SaveStep(s, name, "02d-after-failure-blurb-settled");
        Assert.Equal(FakeJwt(), (string?)addRequest["Data"]?["JwtContent"]);
        Assert.Equal("zdew-test-add-identity", (string?)addRequest["Data"]?["IdentityFilename"]);
        WaitFor(s, By.XPath("//*[@AutomationId='Blurb' and @Name='Unexpected error when adding identity!']"));
        await VerifyScreen(Capture(s), "add-failure-blurb");
        Assert.Equal(0, IdentityRowCount(s));

        // Linger like a user reading the error, then close the blurb so it can't cover the next add.
        await Trace.Settle(1000);
        DismissBlurb(s);

        // -----------------------------------------------------------------
        // Add by JWT, the mock replies with success as "by-jwt-1".
        // -----------------------------------------------------------------
        int addsBefore = CommandCount(s, "AddIdentity");
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentitySuccess("by-jwt-1");
        ClickAddIdentityWithJwt(s);

        WaitForCommand(s, "AddIdentity", addsBefore);
        WaitFor(s, By.XPath("//Text[@Name='by-jwt-1']"));
        SaveStep(s, name, "03c-by-jwt-1-rendered-on-landing");
        Assert.Equal(1, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Add by JWT again, success as "by-jwt-2".
        // -----------------------------------------------------------------
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentitySuccess("by-jwt-2");
        ClickAddIdentityWithJwt(s);

        WaitFor(s, By.XPath("//Text[@Name='by-jwt-2']"));
        SaveStep(s, name, "04c-by-jwt-2-rendered-on-landing");
        Assert.Equal(2, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Open by-jwt-2's details.
        // -----------------------------------------------------------------
        OpenIdentityDetails(s, "by-jwt-2");
        await Trace.Settle(350);
        SaveStep(s, name, "05-by-jwt-2-details");

        // -----------------------------------------------------------------
        // Enable MFA with the ToggleField inside IdentityMFA.
        // -----------------------------------------------------------------
        IWebElement mfaToggle = WaitFor(s, By.XPath(
            "//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, mfaToggle);

        // The mock answers EnableMFA with an enrollment_challenge event, which opens the QR dialog.
        WaitForCommand(s, "EnableMFA", 0);
        await Trace.Settle(600); // QR render
        SaveStep(s, name, "06-mfa-qr-dialog");

        // Enrollment uses SetupCode and AuthSetupButton. AuthCode and AuthButton belong to the re-auth screen.
        IWebElement setupCode = WaitFor(s, By.XPath("//*[@AutomationId='SetupCode']"));

        // -----------------------------------------------------------------
        // Submit the rejected code. DoSetupAuthenticate closes the dialog on a failure.
        // -----------------------------------------------------------------
        setupCode.SendKeys(MockIpcServer.RejectedMfaCode);
        await Trace.Settle(150);
        SaveStep(s, name, "07a-rejected-code-typed");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthSetupButton']")).Click();
        JObject verify = WaitForCommand(s, "VerifyMFA", 0);
        WaitForGone(s, By.XPath("//*[@AutomationId='SetupCode']"));
        await Trace.Settle(150);
        SaveStep(s, name, "07c-after-rejected-dialog-closed");

        Assert.Equal(1, CommandCount(s, "VerifyMFA"));
        Assert.Equal(MockIpcServer.RejectedMfaCode, (string?)verify["Data"]?["Code"]);

        // -----------------------------------------------------------------
        // Enable MFA again. The toggle is off since enrollment didn't finish, and the mock mints a fresh secret.
        // -----------------------------------------------------------------
        IWebElement mfaToggle2 = WaitFor(s, By.XPath(
            "//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        int enablesBefore = CommandCount(s, "EnableMFA");
        ClickAt(s, mfaToggle2);
        WaitForCommand(s, "EnableMFA", enablesBefore);
        await Trace.Settle(650); // QR render
        SaveStep(s, name, "08-mfa-re-enabled-fresh-secret");

        // -----------------------------------------------------------------
        // Reveal the fresh secret with Show Secret and submit a real TOTP code for it.
        // -----------------------------------------------------------------
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='SecretButton']")));
        await Trace.Settle(150);
        SaveStep(s, name, "09-secret-revealed");

        string freshSecret = WaitFor(s, By.XPath("//*[@AutomationId='SecretCode']")).Text;
        // The mock validates codes with the same Totp class the test computes them with, so pin the secret itself.
        Assert.Equal(s.Mock.GetMfaSecret(ByJwt2Identifier), freshSecret);

        string realTotp = Totp.Compute(freshSecret, DateTimeOffset.UtcNow);
        IWebElement setupCode2 = WaitFor(s, By.XPath("//*[@AutomationId='SetupCode']"));
        setupCode2.SendKeys(realTotp);
        await Trace.Settle(150);
        SaveStep(s, name, "10a-real-totp-typed");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthSetupButton']")).Click();

        // On success the enrollment_verification event swaps the setup dialog for the recovery codes.
        WaitForGone(s, By.XPath("//*[@AutomationId='SetupCode']"));

        // -----------------------------------------------------------------
        // Wait for the recovery codes screen by its title label, since its Grid panels have no UIA peers.
        // -----------------------------------------------------------------
        WaitFor(s, By.XPath("//Text[@Name='MFA Recovery Codes']"));
        await Trace.Settle(300);
        SaveStep(s, name, "11-mfa-recovery-codes");

        // -----------------------------------------------------------------
        // Regenerate opens the re-auth screen (ShowMFA type 4), which asks for a TOTP code again.
        // -----------------------------------------------------------------
        IWebElement regen = WaitFor(s, By.XPath("//*[@Name='Regenerate']"));
        ClickAt(s, regen);
        await Trace.Settle(300);
        SaveStep(s, name, "12-regenerate-clicked-auth-prompt");

        IWebElement authCode = WaitFor(s, By.XPath("//*[@AutomationId='AuthCode']"));

        // The secret is unchanged, so a code from it still validates.
        string regenCode = Totp.Compute(freshSecret, DateTimeOffset.UtcNow);
        authCode.SendKeys(regenCode);
        await Trace.Settle(150);
        SaveStep(s, name, "13-regen-totp-typed");

        int generatesBefore = CommandCount(s, "GenerateMFACodes");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthButton']")).Click();

        // Checked separately so a click that never fired can't look like codes that never rendered.
        WaitForCommand(s, "GenerateMFACodes", generatesBefore);

        // The codes are TextBoxes, whose text is the UIA Value rather than Name, so PageSource never contains it.
        WaitUntil(s, "REGEN recovery codes show", TimeSpan.FromSeconds(8),
            () => s.Driver.FindElements(By.XPath("//Edit")).Any(e => (e.Text ?? "").StartsWith("REGEN")));
        await Trace.Settle(300);
        SaveStep(s, name, "14-new-recovery-codes");

        // -----------------------------------------------------------------
        // Close the recovery codes (CloseBlack), back to by-jwt-2's details.
        // -----------------------------------------------------------------
        IWebElement closeBlack = WaitFor(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        ClickAt(s, closeBlack);
        await Trace.Settle(300);
        SaveStep(s, name, "15-mfa-recovery-dismissed");

        // -----------------------------------------------------------------
        // Back to the landing list, where the identity toggles are.
        // -----------------------------------------------------------------
        CloseIdentityDetails(s);
        await Trace.Settle(400);
        SaveStep(s, name, "16-back-on-landing-by-jwt-2-mfa-enabled");

        // -----------------------------------------------------------------
        // Disable by-jwt-2. The mock clears Active and MfaNeeded.
        // -----------------------------------------------------------------
        IWebElement bj2Toggle = WaitFor(s, By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='by-jwt-2']]//*[@AutomationId='ToggleSwitch']"));
        int onOffsBeforeDisable = CommandCount(s, "IdentityOnOff");
        ClickAt(s, bj2Toggle);
        JObject disable = WaitForCommand(s, "IdentityOnOff", onOffsBeforeDisable);
        await Trace.Settle(400);
        SaveStep(s, name, "17-by-jwt-2-disabled");
        Assert.Equal(false, (bool?)disable["Data"]?["OnOff"]);
        Assert.Equal(ByJwt2Identifier, (string?)disable["Data"]?["Identifier"]);

        // -----------------------------------------------------------------
        // Re-enable by-jwt-2. With MfaEnabled persisted, the mock sets MfaNeeded and the row shows MfaRequired.
        // -----------------------------------------------------------------
        bj2Toggle = WaitFor(s, By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='by-jwt-2']]//*[@AutomationId='ToggleSwitch']"));
        int onOffsBeforeEnable = CommandCount(s, "IdentityOnOff");
        ClickAt(s, bj2Toggle);
        JObject enable = WaitForCommand(s, "IdentityOnOff", onOffsBeforeEnable);
        Assert.Equal(true, (bool?)enable["Data"]?["OnOff"]);
        // ZET answers an MFA identity coming back on with status and auth_challenge events, which show MfaRequired.
        WaitFor(s, By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='by-jwt-2']]//*[@AutomationId='MfaRequired']"));
        SaveStep(s, name, "18-by-jwt-2-re-enabled-mfa-needed");

        // -----------------------------------------------------------------
        // Forget by-jwt-2 and confirm. The mock's default reply to RemoveIdentity is success.
        // -----------------------------------------------------------------
        OpenIdentityDetails(s, "by-jwt-2");
        await Trace.Settle(350);

        IWebElement forget = WaitFor(s, By.XPath("//*[@AutomationId='ForgetIdentityButton']"));
        ClickAt(s, forget);
        await Trace.Settle(300);
        SaveStep(s, name, "19-confirm-forget-dialog");
        await VerifyScreen(Capture(s), "forget-confirm");

        IWebElement confirm = WaitFor(s, By.XPath("//*[@AutomationId='ConfirmButton']"));
        ClickAt(s, confirm);

        JObject remove = WaitForCommand(s, "RemoveIdentity", 0);
        WaitForGone(s, By.XPath("//Text[@Name='by-jwt-2']"));
        await Trace.Settle(300);
        SaveStep(s, name, "20-back-to-landing-only-by-jwt-1");

        // -----------------------------------------------------------------
        // Only by-jwt-1 remains.
        // -----------------------------------------------------------------
        Assert.Equal(ByJwt2Identifier, (string?)remove["Data"]?["Identifier"]);
        Assert.Equal(1, IdentityRowCount(s));
        Assert.True(s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-1']")).Count > 0);
        Assert.Empty(s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-2']")));
    }
}
