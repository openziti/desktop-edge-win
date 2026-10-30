using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// With several providers and no default, the row's ext auth click opens a provider menu instead of logging in.
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthMultipleSignersTests
{
    private const string ExtraSignerA = "test_ext_auth_signer_extra_a";
    private const string ExtraSignerB = "test_ext_auth_signer_extra_b";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthMultipleSignersTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneMultipleSignersDefaultPolicyCompletes()
    {
        string name = nameof(EnrollToNoneMultipleSignersDefaultPolicyCompletes);
        const string identityName = "test_ext_auth_multi_default";
        await WithExtraSigners(async _ =>
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            EnterControllerUrl(session, Quickstart.UiControllerUrl);
            JObject needsLogin = JoinEnrolledToNone(session);
            List<string> providers = needsLogin["Id"]!["ExtAuthProviders"]!.Select(provider => (string)provider!).ToList();
            Assert.Subset(new HashSet<string>(providers),
                new HashSet<string> { IntegrationFixture.WorkingSignerName, ExtraSignerA, ExtraSignerB });
            WaitForController(session, ExtAuthRequiredIcon, "the row asks for external auth");
            await VerifyStep(session, Capture(session), name, "01-identity-needs-ext-login");

            OpenProviderMenu(session, providers);
            await VerifyPopupStep(session, CaptureWithPopups(session), name, "02-provider-menu");

            // The first click outside the menu only closes it, which ClickUntil retries past. The UI names a URL
            // identity host_port until a controller event names it.
            OpenIdentityDetails(session, "127.0.0.1_11280");
            await VerifyStep(session, Capture(session), name, "03-provider-list");
            ClickAt(session, WaitFor(session,
                By.XPath($"//List[@AutomationId='ProviderList']/ListItem[@Name='{IntegrationFixture.WorkingSignerName}']")));
            IWebElement isDefault = WaitForId(session, "IsDefaultProvider");
            Assert.False(isDefault.Selected, "a default provider was already set before the test ticked it");
            ClickAt(session, isDefault);
            WaitUntil(session, "the default provider checkbox is ticked", AppResponseTimeout, () => isDefault.Selected);
            await VerifyStep(session, Capture(session), name, "04-default-provider");
            CloseIdentityDetails(session);

            CloseBrowsers();
            string loginUrl = SendExternalAuth(session, IntegrationFixture.WorkingSignerName,
                () => ClickAt(session, WaitFor(session, ExtAuthRequiredIcon)));
            Assert.DoesNotContain(session.Driver.FindElements(ProviderMenuItems), item => item.Displayed);
            await LogInAtIdP(session, loginUrl, $"{identityName}@test.com");
            JObject identityAddedEvent = AssertEnrollmentAdded(session, ExternalAuthLine);
            AssertUrlEnrolledToNoneIdentityFile((string)identityAddedEvent["Id"]!["Identifier"]!);
            WaitUntil(session, "the row stops asking for external auth", ControllerTimeout,
                () => !session.Driver.FindElements(ExtAuthRequiredIcon).Any(icon => icon.Displayed));
            WaitForRowNamed(session, identityName);
            await VerifyStep(session, Capture(session), name, "05-identity-enrolled");
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneMultipleSignersNamedPolicyCompletes()
    {
        string name = nameof(EnrollToNoneMultipleSignersNamedPolicyCompletes);
        const string identityName = "test_ext_auth_multi_named";
        const string policyName = "test_ext_auth_multi_named_policy";
        await WithExtraSigners(async signerIds =>
        {
            List<string> createPolicyArgs = new() { "edge", "create", "auth-policy", policyName, "--primary-ext-jwt-allowed" };
            foreach (string signerId in signerIds)
                createPolicyArgs.AddRange(new[] { "--primary-ext-jwt-allowed-signers", signerId });
            _fixture.Quickstart.RunChecked(createPolicyArgs.ToArray());
            try
            {
                _fixture.Quickstart.RunChecked("edge", "create", "identity", identityName,
                    "--external-id", $"{identityName}@test.com", "-P", policyName);
                try
                {
                    await using AppiumSession session = await LaunchAsync(_fixture, name);
                    EnterControllerUrl(session, Quickstart.UiControllerUrl);
                    JObject needsLogin = JoinEnrolledToNone(session);
                    List<string> providers = needsLogin["Id"]!["ExtAuthProviders"]!.Select(provider => (string)provider!).ToList();
                    Assert.Subset(new HashSet<string>(providers),
                        new HashSet<string> { IntegrationFixture.WorkingSignerName, ExtraSignerA, ExtraSignerB });
                    WaitForController(session, ExtAuthRequiredIcon, "the row asks for external auth");
                    await VerifyStep(session, Capture(session), name, "01-identity-needs-ext-login");

                    OpenProviderMenu(session, providers);
                    await VerifyPopupStep(session, CaptureWithPopups(session), name, "02-provider-menu");

                    By workingSignerItem =
                        By.XPath($"//MenuItem[@Name='{MenuItemName(IntegrationFixture.WorkingSignerName)}']");
                    string loginUrl = SendExternalAuth(session, IntegrationFixture.WorkingSignerName,
                        () => ClickAt(session, WaitFor(session, workingSignerItem)));
                    await LogInAtIdP(session, loginUrl, $"{identityName}@test.com");
                    JObject identityAddedEvent = AssertEnrollmentAdded(session, ExternalAuthLine);
                    AssertUrlEnrolledToNoneIdentityFile((string)identityAddedEvent["Id"]!["Identifier"]!);
                    WaitUntil(session, "the row stops asking for external auth", ControllerTimeout,
                        () => !session.Driver.FindElements(ExtAuthRequiredIcon).Any(icon => icon.Displayed));
                    WaitForRowNamed(session, identityName);
                    await VerifyStep(session, Capture(session), name, "03-identity-enrolled");
                }
                finally
                {
                    _fixture.Quickstart.DeleteIdentity(identityName);
                }
            }
            finally
            {
                // Before WithExtraSigners deletes the signers this policy allows.
                _fixture.Quickstart.RunChecked("edge", "delete", "auth-policy", policyName);
            }
        });
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToCertThroughPickedSigner()
    {
        string name = nameof(EnrollToCertThroughPickedSigner);
        await WithExtraSigners(async _ =>
        {
            // Extra signer A can't finish a login, it only makes the picker show.
            _fixture.Quickstart.UpdateExtJwtSigner(ExtraSignerA, Quickstart.EnrollToNone with { ToCert = true });
            await WithWorkingSigner(_fixture, Quickstart.EnrollToNone with { ToCert = true }, async () =>
            {
                await using AppiumSession session = await LaunchAsync(_fixture, name);
                OpenEnrollChoice(session);
                WaitFor(session, SignerPickerLabel);
                Assert.Empty(session.Driver.FindElements(UserSessionRadio));
                await VerifyStep(session, Capture(session), name, "01-signer-choice");

                IWebElement joinButton = EnrollChoiceJoinButton(session);
                int addsBefore = UiCmdLineCount(session.Relay!, AddIdentityLine);
                joinButton.Click();
                // Nothing marks an ignored click, so the wait is fixed: AddIdentity follows Join within milliseconds.
                Pause(1000);
                Assert.Equal(addsBefore, UiCmdLineCount(session.Relay!, AddIdentityLine));
                WaitFor(session, SignerPickerLabel);

                ClickUntil(session, ById("SignerPicker"), SignerPickerItems);
                await VerifyPopupStep(session, CaptureWithPopups(session), name, "02-signer-list");
                // An item bound through DisplayMemberPath has no UIA Name of its own, only its TextBlock does.
                ClickAt(session, WaitFor(session,
                    By.XPath($"//ListItem[.//Text[@Name='{IntegrationFixture.WorkingSignerName}']]")));
                WaitForGone(session, SignerPickerItems);
                await VerifyStep(session, Capture(session), name, "03-signer-picked");

                await FinishEnrollToCert(session, "test_ext_auth_cert_picked", JoinFromEnrollChoice(session));
                JObject sent = UiCommand(session.Relay!, AddIdentityLine);
                Assert.Equal("cert", (string?)sent["Data"]!["EnrollMode"]);
                Assert.Equal(IntegrationFixture.WorkingSignerName, (string?)sent["Data"]!["Provider"]);
                await VerifyStep(session, Capture(session), name, "04-identity-enrolled");
            });
        });
    }

    private static readonly By SignerPickerItems = By.XPath("//ListItem");

    private static readonly By ProviderMenuItems = By.XPath("//MenuItem");

    /// <summary>A saved default provider would skip the next run's menu, so it is cleared before and after.</summary>
    private async Task WithExtraSigners(Func<IReadOnlyList<string>, Task> body)
    {
        AppUserConfig.RemoveDefaultProvider(IntegrationFixture.WorkingSignerName);
        string extraA = _fixture.Quickstart.CreateExtJwtSigner(ExtraSigner(ExtraSignerA, Dex.ClientIdExtraA));
        try
        {
            string extraB = _fixture.Quickstart.CreateExtJwtSigner(ExtraSigner(ExtraSignerB, Dex.ClientIdExtraB));
            try
            {
                await body(new[] { _fixture.WorkingSignerId, extraA, extraB });
            }
            finally
            {
                _fixture.Quickstart.DeleteExtJwtSigner(ExtraSignerB);
            }
        }
        finally
        {
            _fixture.Quickstart.DeleteExtJwtSigner(ExtraSignerA);
            CloseBrowsers();
            // The body's session has exited by now, so it can't save its copy over this.
            AppUserConfig.RemoveDefaultProvider(IntegrationFixture.WorkingSignerName);
        }
    }

    // The extra signers' issuer and JWKS point nowhere: only the working signer can log in.
    private Quickstart.ExtJwtSigner ExtraSigner(string signerName, string clientId) =>
        new(signerName, $"{Dex.IssuerUrl}-{signerName}", $"{_fixture.Dex.JwksUri}-{signerName}", clientId);

    private static void OpenProviderMenu(AppiumSession session, IReadOnlyList<string> providers)
    {
        CloseBrowsers();
        ClickUntil(session, ExtAuthRequiredIcon, ProviderMenuItems);
        string[] listed = session.Driver.FindElements(ProviderMenuItems).Select(item => item.GetAttribute("Name")).ToArray();
        // The app sorts with List.Sort, the same default comparer OrderBy uses.
        Assert.Equal(providers.OrderBy(provider => provider).Select(MenuItemName), listed);
    }

    private static string SendExternalAuth(AppiumSession session, string provider, Action click)
    {
        JObject reply = SendAndWaitForZetReply(session, ExternalAuthLine, click);
        Assert.Equal(provider, (string?)UiCommand(session.Relay!, ExternalAuthLine)["Data"]!["Provider"]);
        return IdpUrlFromReply(reply);
    }

    // MenuItemAutomationPeer reads a string Header as access key text and drops its first underscore.
    private static string MenuItemName(string header)
    {
        int marker = header.IndexOf('_');
        if (marker < 0)
            return header;
        return header.Remove(marker, 1);
    }
}
