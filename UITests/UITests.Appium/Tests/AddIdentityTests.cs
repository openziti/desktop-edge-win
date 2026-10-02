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

    private readonly IntegrationFixture _fixture;

    public AddIdentityTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task SameJwtTwiceSecondFails()
    {
        string name = nameof(SameJwtTwiceSecondFails);
        const string identityName = "test_add_id_dup_name";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        SaveStep(s, name, "01-identity-added");

        // AddIdentity left the same JWT in the file the app reads.
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => ClickAddIdentityWithJwt(s));
        // The blurb shows on the reply and hides 2.5s later.
        await VerifyStep(Capture(s), name, "02-add-failure-blurb");
        // The blurb shows no detail from ZET, so only the reply proves why the add failed.
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(s));
    }

    [Fact(Timeout = 60000)]
    public async Task WithInvalidJwtFails() => await AssertJwtRejectedByApp(nameof(WithInvalidJwtFails), "this.is.not-a-real-jwt");

    [Fact(Timeout = 60000)]
    public async Task WithEmptyJwtFails() => await AssertJwtRejectedByApp(nameof(WithEmptyJwtFails), "");

    /// <summary>The app parses the JWT itself, so ZET never sees one it rejects.</summary>
    private async Task AssertJwtRejectedByApp(string name, string jwt)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        WriteTestJwt(jwt);
        ClickAddIdentityWithJwt(s);
        WaitFor(s, JwtInvalidError);
        await VerifyStep(Capture(s), name, "01-jwt-invalid");
        Assert.Equal(0, UiCmdLineCount(s.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task WithDeletedIdentityFails()
    {
        string name = nameof(WithDeletedIdentityFails);
        const string identityName = "test_add_id_deleted";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        string jwt = _fixture.Quickstart.GetJwtFromController(identityName);
        _fixture.Quickstart.DeleteIdentity(identityName);
        WriteTestJwt(jwt);
        JObject reply = SendAndWaitForZetReply(s, AddIdentityLine, () => ClickAddIdentityWithJwt(s));
        await VerifyStep(Capture(s), name, "01-add-failure-blurb");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("JWT not accepted by controller", (string?)reply["Error"]);
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task WithValidControllerUrlSucceeds()
    {
        string name = nameof(WithValidControllerUrlSucceeds);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        await VerifyStep(Capture(s), name, "01-url-entered");
        JoinEnrolledToNone(s);
    }

    [Fact(Timeout = 60000)]
    public async Task WithMalformedUrlFails()
    {
        string name = nameof(WithMalformedUrlFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, "not-a-url");
        await VerifyStep(Capture(s), name, "01-url-invalid");
        // The app validates the URL itself, so ZET never sees this one.
        Assert.False(WaitForId(s, "JoinNetworkBtn").Enabled, "Join Network is enabled for a malformed URL");
        Assert.Equal(0, UiCmdLineCount(s.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 60000)]
    public async Task WithNonZitiEndpointFails()
    {
        string name = nameof(WithNonZitiEndpointFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, "https://example.com");
        WaitForId(s, "JoinNetworkBtn").Click();
        // The app's ext-jwt signer discovery fails before it sends anything to ZET, so only the blurb marks the time.
        WaitForBlurb(s, "Unexpected error accessing URL");
        await VerifyStep(Capture(s), name, "01-url-access-error-blurb");
        Assert.Equal(0, UiCmdLineCount(s.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(s));
    }

    [Fact(Timeout = 120000)]
    public async Task SameNameTwiceSecondFails()
    {
        string name = nameof(SameNameTwiceSecondFails);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        EnterControllerUrl(s, Quickstart.UiControllerUrl);
        JoinEnrolledToNone(s);
        SaveStep(s, name, "01-identity-needs-ext-login");
        await AssertSameNameRejected(s, name, "02-add-failure-blurb");
    }
}
