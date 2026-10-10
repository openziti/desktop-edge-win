using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using ZitiDesktopEdge.UITests.MockIpc;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

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

    /// <summary>Enroll, trigger the reauth challenge, then authenticate with the code the enrollment picks.</summary>
    private async Task ReauthAccepts(string name, string identityName, Func<MfaEnrollment, string> pickCode)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        MfaEnrollment enrollment = AddIdentityAndEnrollMfa(_fixture, s, identityName);
        TriggerReauthChallenge(s, identityName);
        await VerifyStep(Capture(s), name, "01-authenticate-row");

        AuthenticateFromRow(s, name, "02-code-typed", identityName, pickCode(enrollment));
        // The mfa_auth_status event reopens details because enrollment opened them for this identity.
        await VerifyStep(Capture(s), name, "03-authenticated-details");
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsValidTotp() =>
        await ReauthAccepts(nameof(ReauthAcceptsValidTotp), "test_mfa_reauth_valid_totp",
            e => Totp.Compute(e.Secret, DateTimeOffset.UtcNow));

    [Fact(Timeout = 180000)]
    public async Task ReauthAcceptsRecoveryCode() =>
        await ReauthAccepts(nameof(ReauthAcceptsRecoveryCode), "test_mfa_reauth_recovery_code",
            e => e.RecoveryCodes[0]);

    [Fact(Timeout = 240000)]
    public async Task ReauthRejectsRecoveryCodeReuse()
    {
        string name = nameof(ReauthRejectsRecoveryCodeReuse);
        const string identityName = "test_mfa_reauth_reused_recovery_code";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        string recoveryCode = AddIdentityAndEnrollMfa(_fixture, s, identityName).RecoveryCodes[0];
        TriggerReauthChallenge(s, identityName);
        AuthenticateFromRow(s, name, "01-code-typed", identityName, recoveryCode);
        CloseIdentityDetails(s);

        TriggerReauthChallenge(s, identityName);
        byte[] rejected = RejectFromRow(s, name, "02-code-reused", identityName, recoveryCode);
        // The recovery code differs every run.
        await VerifyStep(Masked(s, rejected, ById("AuthCode")), name, "03-after-rejection");
    }

    [Fact(Timeout = 180000)]
    public async Task ReauthRejectsInvalidTotp()
    {
        string name = nameof(ReauthRejectsInvalidTotp);
        const string identityName = "test_mfa_reauth_invalid_totp";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityAndEnrollMfa(_fixture, s, identityName);
        TriggerReauthChallenge(s, identityName);
        byte[] rejected = RejectFromRow(s, name, "01-code-typed", identityName, "000000");
        await VerifyStep(rejected, name, "02-after-rejection");
    }
}
