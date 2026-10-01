using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of TestAddIdentityByJwt and TestAddIdentityByUrl in ziti-tunnel-sdk-c tests/integration/add_identity_test.go.
/// The JWT filename subtests have no twin: the UI sends the picked JWT file's name, which Windows never lets hold a path
/// separator or 5000 characters. afterJwtSameNameFails has no twin either: the UI names a URL identity host_port and a
/// JWT identity after its file, which test mode fixes as zdew-test-add-identity, so the two never collide.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class AddIdentityTests
{
    private static readonly By JwtInvalidError = By.XPath("//*[@AutomationId='ErrorTitle' and @Name='JWT Invalid']");
    private const string AddFailureBlurb = "Unexpected error when adding identity!";

    private readonly IntegrationFixture _fixture;

    public AddIdentityTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task SameJwtTwiceSecondFails()
    {
        Trace.Begin();
        string name = nameof(SameJwtTwiceSecondFails);
        const string identityName = "test_add_id_dup_name";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        SaveStep(s, name, "01-identity-added");

        // AddIdentity left the same JWT in the file the app reads.
        ClickAddIdentityWithJwt(s);
        WaitForBlurb(s, AddFailureBlurb);
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so one capture serves both.
        byte[] blurb = Capture(s);
        SaveStep(blurb, name, "02-add-failure-blurb");
        await VerifyScreen(blurb, "add-failure-blurb");
        // The blurb shows no detail from ZET, so only the reply proves why the add failed.
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"AddIdentity\"");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(s));
    }

    [Fact(Timeout = 60000)]
    public async Task WithInvalidJwtFails()
    {
        Trace.Begin();
        string name = nameof(WithInvalidJwtFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        WriteTestJwt("this.is.not-a-real-jwt");
        ClickAddIdentityWithJwt(s);
        // The app parses the JWT itself, so ZET never sees this one.
        WaitFor(s, JwtInvalidError);
        await Trace.Settle(350);
        SaveStep(s, name, "01-jwt-invalid");
        await VerifyScreen(Capture(s), "jwt-invalid");
        Assert.False(UiSent(s.Relay!, "AddIdentity"));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 60000)]
    public async Task WithEmptyJwtFails()
    {
        Trace.Begin();
        string name = nameof(WithEmptyJwtFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        WriteTestJwt("");
        ClickAddIdentityWithJwt(s);
        WaitFor(s, JwtInvalidError);
        await Trace.Settle(350);
        SaveStep(s, name, "01-jwt-invalid");
        await VerifyScreen(Capture(s), "jwt-invalid");
        Assert.False(UiSent(s.Relay!, "AddIdentity"));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task WithDeletedIdentityFails()
    {
        Trace.Begin();
        string name = nameof(WithDeletedIdentityFails);
        const string identityName = "test_add_id_deleted";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        string jwt = _fixture.Quickstart.GetJwtFromController(identityName);
        _fixture.Quickstart.DeleteIdentity(identityName);
        WriteTestJwt(jwt);
        ClickAddIdentityWithJwt(s);
        WaitForBlurb(s, AddFailureBlurb);
        // ShowBlurbAsync hides the blurb 2.5s after showing it, so one capture serves both.
        byte[] blurb = Capture(s);
        SaveStep(blurb, name, "01-add-failure-blurb");
        await VerifyScreen(blurb, "add-failure-blurb");
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"AddIdentity\"");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("JWT not accepted by controller", (string?)reply["Error"]);
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task WithValidControllerUrlSucceeds()
    {
        Trace.Begin();
        string name = nameof(WithValidControllerUrlSucceeds);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        await Trace.Settle(350);
        SaveStep(s, name, "01-url-entered");
        await VerifyScreen(Capture(s), "url-entered");

        JoinEnrolledToNone(s);
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-needs-ext-login");
        await VerifyScreen(Capture(s), "identity-needs-ext-login");
    }

    [Fact(Timeout = 60000)]
    public async Task WithMalformedUrlFails()
    {
        Trace.Begin();
        string name = nameof(WithMalformedUrlFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, "not-a-url");
        await Trace.Settle(350);
        SaveStep(s, name, "01-url-invalid");
        await VerifyScreen(Capture(s), "url-invalid");
        // The app validates the URL itself, so ZET never sees this one.
        Assert.False(WaitForId(s, "JoinNetworkBtn").Enabled, "Join Network is enabled for a malformed URL");
        Assert.False(UiSent(s.Relay!, "AddIdentity"));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 60000)]
    public async Task WithNonZitiEndpointFails()
    {
        Trace.Begin();
        string name = nameof(WithNonZitiEndpointFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, "https://example.com");
        WaitForId(s, "JoinNetworkBtn").Click();
        // The app's ext-jwt signer discovery fails before it sends anything to ZET.
        WaitForBlurb(s, "Unexpected error accessing URL");
        byte[] blurb = Capture(s);
        SaveStep(blurb, name, "01-url-access-error-blurb");
        await VerifyScreen(blurb, "url-access-error-blurb");
        Assert.False(UiSent(s.Relay!, "AddIdentity"));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task SameNameTwiceSecondFails()
    {
        Trace.Begin();
        string name = nameof(SameNameTwiceSecondFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentityByUrl(s, Quickstart.UiControllerUrl);
        SaveStep(s, name, "01-identity-needs-ext-login");

        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        WaitForId(s, "JoinNetworkBtn").Click();
        WaitForBlurb(s, AddFailureBlurb);
        byte[] blurb = Capture(s);
        SaveStep(blurb, name, "02-add-failure-blurb");
        await VerifyScreen(blurb, "add-failure-blurb");
        JObject reply = ZetReplyTo(s.Relay!, AddIdentityLine);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(s));
    }
}
