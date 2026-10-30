using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class MfaReauthenticationTests
{
    private readonly IntegrationFixture _fixture;

    public MfaReauthenticationTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    private async Task ReauthAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession session = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, session, identityName);
        TriggerReauthChallenge(session, identityName);
        await VerifyStep(session, Capture(session), name, "01-authenticate-row");

        AuthenticateFromRow(session, name, "02-code-typed", identityName, pickCode(enrollment));
        // The mfa_auth_status event reopens details because enrollment opened them for this identity.
        await VerifyStep(session, Capture(session), name, "03-authenticated-details");
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsValidTotp() =>
        await ReauthAccepts(nameof(ReauthAcceptsValidTotp), "test_mfa_reauth_valid_totp",
            enrollment => Totp.Compute(enrollment.Secret, DateTimeOffset.UtcNow));

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsRecoveryCode() =>
        await ReauthAccepts(nameof(ReauthAcceptsRecoveryCode), "test_mfa_reauth_recovery_code",
            enrollment => enrollment.RecoveryCodes[0]);

    [Fact(Timeout = 240000)]
    public async Task ReauthRejectsRecoveryCodeReuse()
    {
        string name = nameof(ReauthRejectsRecoveryCodeReuse);
        const string identityName = "test_mfa_reauth_reused_recovery_code";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        string recoveryCode = AddIdentityAndEnrollMfa(_fixture, session, identityName).RecoveryCodes[0];
        TriggerReauthChallenge(session, identityName);
        AuthenticateFromRow(session, name, "01-code-typed", identityName, recoveryCode);
        CloseIdentityDetails(session);

        TriggerReauthChallenge(session, identityName);
        byte[] rejected = RejectFromRow(session, name, "02-code-reused", identityName, recoveryCode);
        // The recovery code differs every run.
        await VerifyStep(session, Masked(session, rejected, ById("AuthCode")), name, "03-after-rejection");
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthRejectsInvalidTotp()
    {
        string name = nameof(ReauthRejectsInvalidTotp);
        const string identityName = "test_mfa_reauth_invalid_totp";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, session, identityName);
        TriggerReauthChallenge(session, identityName);
        byte[] rejected = RejectFromRow(session, name, "01-code-typed", identityName, "000000");
        await VerifyStep(session, rejected, name, "02-after-rejection");
    }
}
