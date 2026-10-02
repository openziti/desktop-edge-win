using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestExternalAuthMultipleSigners in ziti-tunnel-sdk-c tests/integration/external_auth_test.go. With more
/// than one provider and no default, the row's ext auth click opens a menu of providers instead of logging in. Once
/// identity details sets a default, the click logs in to it.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class ExternalAuthMultipleSignersTests
{
    private const string ExtraSignerA = "test_ext_auth_signer_extra_a";
    private const string ExtraSignerB = "test_ext_auth_signer_extra_b";

    private readonly IntegrationFixture _fixture;

    public ExternalAuthMultipleSignersTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task EnrollToNoneMultipleSignersDefaultPolicyCompletes()
    {
        string name = nameof(EnrollToNoneMultipleSignersDefaultPolicyCompletes);
        const string identityName = "test_ext_auth_multi_default";
        await WithExtraSigners(async () =>
        {
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            PrepareTestWindow(s);
            EnterControllerUrl(s, Quickstart.UiControllerUrl);
            JObject needsLogin = JoinEnrolledToNone(s);
            List<string> providers = needsLogin["Id"]!["ExtAuthProviders"]!.Select(p => (string)p!).ToList();
            Assert.Subset(new HashSet<string>(providers),
                new HashSet<string> { IntegrationFixture.WorkingSignerName, ExtraSignerA, ExtraSignerB });
            WaitForController(s, ExtAuthRequiredIcon, "the row asks for external auth");
            await VerifyStep(Capture(s), name, "01-identity-needs-ext-login");

            OpenProviderMenu(s, providers);
            await VerifyStep(CaptureWithPopups(s), name, "02-provider-menu");

            // The first click outside the menu only closes it, which ClickUntil retries past.
            OpenIdentityDetails(s, UrlIdentityName);
            await VerifyStep(Capture(s), name, "03-provider-list");
            SetDefaultProvider(s, IntegrationFixture.WorkingSignerName);
            await VerifyStep(Capture(s), name, "04-default-provider");
            CloseIdentityDetails(s);

            string loginUrl = LoginFromRowWithDefault(s, IntegrationFixture.WorkingSignerName);
            await Dex.DriveIdPFlowAsync(loginUrl, $"{identityName}@test.com");
            JObject added = AssertEnrollmentAdded(s, ExternalAuthLine);
            AssertUrlEnrolledToNoneIdentityFile((string)added["Id"]!["Identifier"]!);
            WaitUntil(s, "the row stops asking for external auth", ControllerTimeout,
                () => !s.Driver.FindElements(ExtAuthRequiredIcon).Any(e => e.Displayed));
            await VerifyStep(Capture(s), name, "05-identity-enrolled");
        });
    }

    // The UI names a URL identity host_port until a controller event names it.
    private const string UrlIdentityName = "127.0.0.1_11280";

    private static readonly By ProviderMenuItems = By.XPath("//MenuItem");

    /// <summary>
    /// ZET's setupExtraExtJwtSigners for the body, deleted after it. A default provider the body sets persists in the
    /// user's user.config and would skip the next run's menu, so it is cleared before and after.
    /// </summary>
    private async Task WithExtraSigners(Func<Task> body)
    {
        AppUserConfig.RemoveDefaultProvider(IntegrationFixture.WorkingSignerName);
        _fixture.Quickstart.CreateExtJwtSigner(ExtraSigner(ExtraSignerA, Dex.ClientIdExtraA));
        try
        {
            _fixture.Quickstart.CreateExtJwtSigner(ExtraSigner(ExtraSignerB, Dex.ClientIdExtraB));
            try
            {
                await body();
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

    // Like ZET's, the extra signers' issuer and JWKS point nowhere: only the working signer can log in.
    private Quickstart.ExtJwtSigner ExtraSigner(string signerName, string clientId) =>
        new(signerName, $"{Dex.IssuerUrl}-{signerName}", $"{_fixture.Dex.JwksUri}-{signerName}", clientId);

    /// <summary>Click the row's ext auth icon and assert the menu it opens lists every provider, sorted.</summary>
    private static void OpenProviderMenu(AppiumSession s, IReadOnlyList<string> providers)
    {
        // The browser opened for an earlier IdP URL can cover the icon, and the click is a real mouse click.
        CloseBrowsers();
        ClickUntil(s, ExtAuthRequiredIcon, ProviderMenuItems);
        string[] listed = s.Driver.FindElements(ProviderMenuItems).Select(item => item.GetAttribute("Name")).ToArray();
        // The app sorts with List.Sort, the same default comparer OrderBy uses.
        Assert.Equal(providers.OrderBy(p => p).Select(MenuItemName), listed);
    }

    /// <summary>On identity details, select provider in Configured Providers and tick "Default provider?".</summary>
    private static void SetDefaultProvider(AppiumSession s, string provider)
    {
        ClickAt(s, WaitFor(s, By.XPath($"//List[@AutomationId='ProviderList']/ListItem[@Name='{provider}']")));
        IWebElement isDefault = WaitForId(s, "IsDefaultProvider");
        Assert.False(isDefault.Selected, "a default provider was already set before the test ticked it");
        ClickAt(s, isDefault);
        WaitUntil(s, "the default provider checkbox is ticked", TimeSpan.FromSeconds(5), () => isDefault.Selected);
    }

    /// <summary>
    /// ZET's GetExternalAuthURL from the row once provider is the default: the icon logs in to it without the menu.
    /// Returns the IdP URL from ZET's reply.
    /// </summary>
    private static string LoginFromRowWithDefault(AppiumSession s, string provider)
    {
        CloseBrowsers();
        JObject reply = SendAndWaitForZetReply(s, ExternalAuthLine, () => ClickAt(s, WaitFor(s, ExtAuthRequiredIcon)));
        Assert.Equal(provider, (string?)UiCommand(s.Relay!, ExternalAuthLine)["Data"]!["Provider"]);
        Assert.DoesNotContain(s.Driver.FindElements(ProviderMenuItems), item => item.Displayed);
        Assert.Equal(0, (int?)reply["Code"]);
        string? url = (string?)reply["Data"]?["url"];
        Assert.False(string.IsNullOrEmpty(url), $"ExternalAuth reply has no Data.url: {reply}");
        return url!;
    }

    /// <summary>
    /// A MenuItem's UIA Name for a string Header: MenuItemAutomationPeer reads it as access key text and drops its first
    /// underscore. The provider names here never hold a doubled one, which it would collapse instead.
    /// </summary>
    private static string MenuItemName(string header)
    {
        int marker = header.IndexOf('_');
        return marker < 0 ? header : header.Remove(marker, 1);
    }
}
