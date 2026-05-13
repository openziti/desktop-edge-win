using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One long end-to-end UI flow test exercising the JWT enrollment path:
///   1. Launch UI on an empty-identities fixture.
///   2. Add by JWT, mock-respond FAILURE -> expect no identity added.
///   3. Add by JWT, mock-respond SUCCESS as "by-jwt-1" -> expect 1 identity.
///   4. Add by JWT again, SUCCESS as "by-jwt-2" -> expect 2 identities.
///   5. Open by-jwt-2's details.
///   6. Click the MFA toggle to enable -> QR setup dialog appears.
///   7. Type 666666, click Authenticate -> mock returns Failure
///                                          (textbox clears, dialog stays open).
///   8. Type 123456, click Authenticate -> mock returns Success
///                                          (dialog closes).
///   9. Close identity details.
///  10. Assert 2 identities still rendered on landing.
///
/// Notes on the JWT bypass:
///   The real "Add Identity -> With JWT" path pops an OS OpenFileDialog. WPF
///   under ZDEW_UI_TEST=1 instead reads from %TEMP%\zdew-test-add-identity.jwt
///   (see MainWindow.AddIdentity_Click). Each test step writes a fake JWT with
///   em=ott (network enrollment) before triggering the click.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "AddIdentityFlow")]
public class AddIdentityFlowTests
{
    private static string TestJwtPath =>
        Path.Combine(Path.GetTempPath(), "zdew-test-add-identity.jwt");

    /// <summary>
    /// Build a fake JWT whose payload has all the fields the WPF JWT parser
    /// references. The WPF reads `em` to choose an enrollment path ("ott"
    /// routes to AddId -> AddIdentity IPC), and a DEBUG-only Console.WriteLine
    /// reads iss/sub/jti/aud and throws if any are null. Including all five.
    /// </summary>
    private static string FakeJwt()
    {
        var payload = "{\"iss\":\"mock\",\"sub\":\"mock\",\"jti\":\"mock\",\"aud\":[\"mock\"],\"em\":\"ott\"}";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        return $"header.{b64}.signature";
    }

    private static void WriteFakeJwt() => File.WriteAllText(TestJwtPath, FakeJwt());

    /// <summary>
    /// Click the "Add Identity" affordance then the "With JWT" context-menu
    /// item, capturing screenshots at each intermediate state so the gallery
    /// shows: (a) context menu open, (b) right after With-JWT click. The
    /// stepPrefix is the caller's outer step label so each substep gets its
    /// own image (e.g. "02a", "02b").
    /// </summary>
    private static void ClickAddIdentityWithJwt(AppiumSession s, string testName, string stepPrefix)
    {
        // The StackPanel "AddIdAreaButton" has no UIA peer; the "ADD" / "IDENTITY"
        // labels inside it do, and MouseLeftButtonUp bubbles up.
        var addText = WaitFor(s, By.XPath("//Text[@Name='ADD']"));
        ClickAt(s, addText);

        // The context menu surfaces "With JWT" / "With URL" entries.
        var withJwt = WaitFor(s, By.XPath("//*[@Name='With JWT']"));
        ClickAt(s, withJwt);
    }

    /// <summary>Count the IdentityItem rows currently in the landing tree.</summary>
    private static int IdentityRowCount(AppiumSession s) =>
        s.Driver.FindElements(By.XPath("//Custom[@ClassName='IdentityItem']")).Count;

    [Fact(Timeout = 60000)]
    [Trait("Category", "AddIdentityFlow")]
    public async Task AddIdentityByJwt_FailureThenSuccessTwice_EnableMfa_ShowsTwoIdentities()
    {
        Trace.Begin();
        var name = nameof(AddIdentityByJwt_FailureThenSuccessTwice_EnableMfa_ShowsTwoIdentities);

        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "no-identities.json");
        WaitForId(s, "ConnectLabel");

        // Detach from tray, move up + grow height for a normal taskbar window
        // that doesn't crop menus. See TestHelpers.PrepareTestWindow.
        await PrepareTestWindow(s);
        SaveStep(s, name, "01-window-prepared");

        Assert.Equal(0, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Step 1: Add by JWT, mock-respond FAILURE.
        // -----------------------------------------------------------------
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentityFailure("simulated mock-side enrollment failure");
        ClickAddIdentityWithJwt(s, name, "02");

        // Wait for the AddIdentity command to land on the mock; the WPF will
        // surface a blurb on failure.
        var t0 = DateTime.UtcNow;
        while (DateTime.UtcNow - t0 < TimeSpan.FromSeconds(5))
        {
            if (s.Mock.ReceivedCommandNames.Contains("AddIdentity")) break;
            await Task.Delay(100);
        }
        await Trace.Settle(500); // let blurb render
        SaveStep(s, name, "02d-after-failure-blurb-settled");
        Assert.Contains("AddIdentity", s.Mock.ReceivedCommandNames);
        Assert.Equal(0, IdentityRowCount(s));

        // Linger 1s on the blurb (matches a real user reading the error), then
        // click its X. Subsequent JWT-2 add should not have the blurb in the
        // way. No capture here -- the dismissed state is just landing again.
        await Trace.Settle(1000);
        DismissBlurb(s);

        // -----------------------------------------------------------------
        // Step 2: Add by JWT, mock-respond SUCCESS as "by-jwt-1".
        // -----------------------------------------------------------------
        var addsBefore = s.Mock.ReceivedCommandNames.Count(c => c == "AddIdentity");
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentitySuccess("by-jwt-1");
        ClickAddIdentityWithJwt(s, name, "03");

        // Wait for another AddIdentity round-trip + the new row to render.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var hasRow = s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-1']")).Count > 0;
            if (hasRow) break;
            await Task.Delay(150);
        }
        SaveStep(s, name, "03c-by-jwt-1-rendered-on-landing");
        Assert.True(s.Mock.ReceivedCommandNames.Count(c => c == "AddIdentity") > addsBefore);
        Assert.Equal(1, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Step 3: Add by JWT, SUCCESS as "by-jwt-2".
        // -----------------------------------------------------------------
        WriteFakeJwt();
        s.Mock.EnqueueAddIdentitySuccess("by-jwt-2");
        ClickAddIdentityWithJwt(s, name, "04");

        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var hasRow = s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-2']")).Count > 0;
            if (hasRow) break;
            await Task.Delay(150);
        }
        SaveStep(s, name, "04c-by-jwt-2-rendered-on-landing");
        Assert.Equal(2, IdentityRowCount(s));

        // -----------------------------------------------------------------
        // Step 4: Open by-jwt-2 details.
        // -----------------------------------------------------------------
        OpenIdentityDetails(s, "by-jwt-2");
        await Trace.Settle(350);
        SaveStep(s, name, "05-by-jwt-2-details");

        // -----------------------------------------------------------------
        // Step 5: Click MFA toggle (ToggleField inside IdentityMFA) to enable.
        // -----------------------------------------------------------------
        var mfaToggle = WaitFor(s, By.XPath(
            "//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        ClickAt(s, mfaToggle);

        // Wait for EnableMFA on the wire (mock pushes enrollment_challenge in
        // response, which surfaces the QR / authenticator code dialog).
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("EnableMFA")) break;
            await Task.Delay(50);
        }
        await Trace.Settle(600); // QR render
        SaveStep(s, name, "06-mfa-qr-dialog");

        // Enrollment screen (MFASetup) uses SetupCode + AuthSetupButton, NOT
        // AuthCode/AuthButton (which are for the disable / re-auth flow).
        var setupCode = WaitFor(s, By.XPath("//*[@AutomationId='SetupCode']"));

        // -----------------------------------------------------------------
        // Step 6: Attempt #1 -- type 666666 directly without revealing the
        // secret (a real user with the wrong code wouldn't bother). The WPF
        // DoSetupAuthenticate handler closes the dialog on both branches, so
        // after the rejection the dialog disappears entirely.
        // -----------------------------------------------------------------
        setupCode.SendKeys(MockIpcServer.RejectedMfaCode);
        await Trace.Settle(150);
        SaveStep(s, name, "07a-rejected-code-typed");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthSetupButton']")).Click();
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Contains("VerifyMFA")) break;
            await Task.Delay(50);
        }

        // Wait for the dialog to close (SetupCode disappears from the tree).
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Driver.FindElements(By.XPath("//*[@AutomationId='SetupCode']")).Count == 0) break;
            await Task.Delay(100);
        }
        await Trace.Settle(150);
        SaveStep(s, name, "07c-after-rejected-dialog-closed");

        var verifyCount = s.Mock.ReceivedCommandNames.Count(c => c == "VerifyMFA");
        Assert.True(verifyCount >= 1, $"Expected at least one VerifyMFA call, got {verifyCount}");

        // -----------------------------------------------------------------
        // Step 7: Re-enable MFA. The IdentityMFA toggle is back in the OFF
        // state (since the previous enrollment didn't complete). Clicking it
        // re-fires EnableMFA, which mints a fresh secret on the mock side.
        // -----------------------------------------------------------------
        var mfaToggle2 = WaitFor(s, By.XPath(
            "//*[@AutomationId='IdentityMFA']//*[@AutomationId='ToggleField']"));
        var enablesBefore = s.Mock.ReceivedCommandNames.Count(c => c == "EnableMFA");
        ClickAt(s, mfaToggle2);
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Count(c => c == "EnableMFA") > enablesBefore) break;
            await Task.Delay(50);
        }
        await Trace.Settle(650); // QR render (+50ms over the typical 600 so the
                                 // setup dialog is fully drawn before capture)
        SaveStep(s, name, "08-mfa-re-enabled-fresh-secret");

        // -----------------------------------------------------------------
        // Step 8: Click "Show Secret" to reveal the SecretCode TextBox, read
        // the FRESH secret the mock just minted, compute TOTP, submit.
        // -----------------------------------------------------------------
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='SecretButton']")));
        await Trace.Settle(150);
        SaveStep(s, name, "09-secret-revealed");

        var freshSecret = WaitFor(s, By.XPath("//*[@AutomationId='SecretCode']")).Text;
        Assert.False(string.IsNullOrEmpty(freshSecret),
            "Expected SecretCode to contain a base32 secret after Show Secret click");

        var realTotp = Totp.Compute(freshSecret);
        var setupCode2 = WaitFor(s, By.XPath("//*[@AutomationId='SetupCode']"));
        setupCode2.SendKeys(realTotp);
        await Trace.Settle(150);
        SaveStep(s, name, "10a-real-totp-typed");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthSetupButton']")).Click();

        // Wait for the SetupCode entry to disappear (DoSetupAuthenticate calls
        // OnClose on both branches, then the enrollment_verification event
        // re-shows MFASetup in recovery-codes mode via ShowMFARecoveryCodes).
        // No capture between the dialog close and the recovery codes screen --
        // the next interesting state is "MFA Recovery Codes" appearing.
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Driver.FindElements(By.XPath("//*[@AutomationId='SetupCode']")).Count == 0) break;
            await Task.Delay(100);
        }

        // -----------------------------------------------------------------
        // Step 11: After enrollment_verification arrives, WPF re-shows MFASetup
        // in recovery-codes mode (MFARecoveryArea visible) via
        // ShowMFARecoveryCodes. Wait for the "MFA Recovery Codes" title Label
        // (Grid panels don't surface UIA peers; the title Label is the reliable
        // anchor). Hard-assert the screen is actually up before capturing.
        // -----------------------------------------------------------------
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        bool recoveryShown = false;
        while (DateTime.UtcNow < deadline)
        {
            if (s.Driver.FindElements(By.XPath("//Text[@Name='MFA Recovery Codes']")).Count > 0)
            {
                recoveryShown = true; break;
            }
            await Task.Delay(100);
        }
        await Trace.Settle(300); // animation settle
        SaveStep(s, name, "11-mfa-recovery-codes");
        Assert.True(recoveryShown,
            "Expected 'MFA Recovery Codes' screen to appear after successful enrollment.");

        // -----------------------------------------------------------------
        // Step 12: Click "Regenerate". WPF MFAScreen.RegenerateCodes calls
        // ShowMFA(zid, 4) which surfaces the auth (Type 4) screen requiring
        // the user to enter their TOTP again. The auth screen uses AuthCode
        // + AuthButton (NOT SetupCode/AuthSetupButton).
        // -----------------------------------------------------------------
        var regen = WaitFor(s, By.XPath("//*[@Name='Regenerate']"));
        ClickAt(s, regen);
        await Trace.Settle(300);
        SaveStep(s, name, "12-regenerate-clicked-auth-prompt");

        // Sanity: the Type 4 auth screen (AuthCode + AuthButton) should be up.
        var authCode = WaitFor(s, By.XPath("//*[@AutomationId='AuthCode']"));

        // Type the CURRENT TOTP (mock didn't rotate the secret) and submit.
        var regenCode = Totp.Compute(freshSecret);
        authCode.SendKeys(regenCode);
        await Trace.Settle(150);
        SaveStep(s, name, "13-regen-totp-typed");

        var genBefore = s.Mock.ReceivedCommandNames.Count(c => c == "GenerateMFACodes");
        WaitFor(s, By.XPath("//*[@AutomationId='AuthButton']")).Click();

        // First wait for GenerateMFACodes to land on the wire so we know the
        // click actually fired DoAuthenticate -- separating the two failure
        // modes (click didn't dispatch vs UI didn't render new codes).
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Count(c => c == "GenerateMFACodes") > genBefore) break;
            await Task.Delay(50);
        }
        Assert.True(s.Mock.ReceivedCommandNames.Count(c => c == "GenerateMFACodes") > genBefore,
            "Clicking Authenticate on the regenerate screen should send GenerateMFACodes IPC.");

        // Then wait for the recovery codes screen to come back with the new
        // REGEN-prefixed codes.
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Driver.PageSource.Contains("REGEN")) break;
            await Task.Delay(150);
        }
        await Trace.Settle(300);
        SaveStep(s, name, "14-new-recovery-codes");
        Assert.Contains("REGEN", s.Driver.PageSource);

        // -----------------------------------------------------------------
        // Step 13: Dismiss the recovery codes overlay via MFAScreen's close X
        // (CloseBlack). Then we're back on by-jwt-2's IdentityDetails.
        // -----------------------------------------------------------------
        var closeBlack = WaitFor(s, By.XPath("//*[@AutomationId='CloseBlack']"));
        ClickAt(s, closeBlack);
        await Trace.Settle(300);
        SaveStep(s, name, "15-mfa-recovery-dismissed");

        // -----------------------------------------------------------------
        // Step 14: Close details to go back to landing so we can drive the
        // identity-row toggle. Capture the post-MFA-enable landing.
        // -----------------------------------------------------------------
        CloseIdentityDetails(s);
        await Trace.Settle(400);
        SaveStep(s, name, "16-back-on-landing-by-jwt-2-mfa-enabled");

        // -----------------------------------------------------------------
        // Step 15: Disable by-jwt-2 via its ToggleSwitch on the landing row.
        // Mock flips Active=false on the cached identity and clears MfaNeeded.
        // -----------------------------------------------------------------
        var bj2Toggle = WaitFor(s, By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='by-jwt-2']]//*[@AutomationId='ToggleSwitch']"));
        var onOffCountBeforeDisable = s.Mock.ReceivedCommandNames.Count(c => c == "IdentityOnOff");
        ClickAt(s, bj2Toggle);
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Count(c => c == "IdentityOnOff") > onOffCountBeforeDisable) break;
            await Task.Delay(50);
        }
        await Trace.Settle(400);
        SaveStep(s, name, "17-by-jwt-2-disabled");

        // -----------------------------------------------------------------
        // Step 16: Re-enable by-jwt-2. Mock now returns Active=true AND
        // MfaNeeded=true (because MfaEnabled was persisted earlier). The
        // identity/updated event surfaces the MfaRequired icon on the row.
        // -----------------------------------------------------------------
        bj2Toggle = WaitFor(s, By.XPath(
            "//Custom[@ClassName='IdentityItem' and .//Text[@Name='by-jwt-2']]//*[@AutomationId='ToggleSwitch']"));
        var onOffCountBeforeEnable = s.Mock.ReceivedCommandNames.Count(c => c == "IdentityOnOff");
        ClickAt(s, bj2Toggle);
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Mock.ReceivedCommandNames.Count(c => c == "IdentityOnOff") > onOffCountBeforeEnable) break;
            await Task.Delay(50);
        }
        await Trace.Settle(500); // identity/updated event + UI repaint
        SaveStep(s, name, "18-by-jwt-2-re-enabled-mfa-needed");

        // -----------------------------------------------------------------
        // Step 17: Open by-jwt-2 details to click "Forget This Identity"
        // -> "Confirm". WPF sends RemoveIdentity IPC (default mock reply =
        // Success) and removes the identity from its local list.
        // -----------------------------------------------------------------
        OpenIdentityDetails(s, "by-jwt-2");
        await Trace.Settle(350);

        var forget = WaitFor(s, By.XPath("//*[@AutomationId='ForgetIdentityButton']"));
        ClickAt(s, forget);
        await Trace.Settle(300);
        SaveStep(s, name, "19-confirm-forget-dialog");

        var confirm = WaitFor(s, By.XPath("//*[@AutomationId='ConfirmButton']"));
        ClickAt(s, confirm);

        // Wait for by-jwt-2 to disappear from the UIA tree.
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            if (s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-2']")).Count == 0) break;
            await Task.Delay(100);
        }
        await Trace.Settle(300);
        SaveStep(s, name, "20-back-to-landing-only-by-jwt-1");

        // -----------------------------------------------------------------
        // Step 18: Final state -- only by-jwt-1 should remain.
        // -----------------------------------------------------------------
        Assert.Equal(1, IdentityRowCount(s));
        Assert.True(s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-1']")).Count > 0);
        Assert.Empty(s.Driver.FindElements(By.XPath("//Text[@Name='by-jwt-2']")));
    }
}
