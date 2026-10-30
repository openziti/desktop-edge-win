using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class AddIdentityTests
{
    private readonly IntegrationFixture _fixture;

    public AddIdentityTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task SameJwtTwiceSecondFails()
    {
        string name = nameof(SameJwtTwiceSecondFails);
        const string identityName = "test_add_id_dup_name";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        SaveStep(session, name, "01-identity-added");

        // AddIdentity left the same JWT in the file the app reads.
        JObject reply = SendAndWaitForZetReply(session, AddIdentityLine, () => ClickAddIdentityWithJwt(session));
        await VerifyStep(session, CaptureBlurbOnReply(session, AddIdentityLine), name, "02-add-failure-blurb");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("identity exists with the same name", (string?)reply["Error"]);
        Assert.Equal(1, IdentityRowCount(session));
    }

    [Fact(Timeout = 60000)]
    public async Task WithInvalidJwtFails() => await AssertJwtRejectedByApp(nameof(WithInvalidJwtFails), "this.is.not-a-real-jwt");

    [Fact(Timeout = 60000)]
    public async Task WithEmptyJwtFails() => await AssertJwtRejectedByApp(nameof(WithEmptyJwtFails), "");

    private async Task AssertJwtRejectedByApp(string name, string jwt)
    {
        await using AppiumSession session = await LaunchAsync(_fixture, name);
        WriteTestJwt(jwt);
        ClickAddIdentityWithJwt(session);
        WaitFor(session, ErrorTitle("JWT Invalid"));
        await VerifyStep(session, Capture(session), name, "01-jwt-invalid");
        Assert.Equal(0, UiCmdLineCount(session.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(session));
    }

    [Fact(Timeout = 120000)]
    public async Task WithDeletedIdentityFails()
    {
        string name = nameof(WithDeletedIdentityFails);
        const string identityName = "test_add_id_deleted";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        string jwt = _fixture.Quickstart.GetJwtFromController(identityName);
        _fixture.Quickstart.DeleteIdentity(identityName);
        WriteTestJwt(jwt);
        JObject reply = SendAndWaitForZetReply(session, AddIdentityLine, () => ClickAddIdentityWithJwt(session));
        await VerifyStep(session, CaptureBlurbOnReply(session, AddIdentityLine), name, "01-add-failure-blurb");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("JWT not accepted by controller", (string?)reply["Error"]);
        Assert.Equal(0, IdentityRowCount(session));
    }

    [Fact(Timeout = 120000)]
    public async Task WithValidControllerUrlSucceeds()
    {
        string name = nameof(WithValidControllerUrlSucceeds);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        await VerifyStep(session, Capture(session), name, "01-url-entered");
        JoinEnrolledToNone(session);
    }

    [Fact(Timeout = 60000)]
    public async Task WithMalformedUrlFails()
    {
        string name = nameof(WithMalformedUrlFails);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, "not-a-url");
        await VerifyStep(session, Capture(session), name, "01-url-invalid");
        Assert.False(WaitForId(session, "JoinNetworkBtn").Enabled, "Join Network is enabled for a malformed URL");
        Assert.Equal(0, UiCmdLineCount(session.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(session));
    }

    [Fact(Timeout = 60000)]
    public async Task WithNonZitiEndpointFails()
    {
        string name = nameof(WithNonZitiEndpointFails);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, "https://example.com");
        WaitForId(session, "JoinNetworkBtn").Click();
        // The app's ext-jwt signer discovery fails before it sends anything to ZET, so only the blurb marks the time.
        WaitForBlurb(session, "Unexpected error accessing URL", ControllerTimeout);
        await VerifyStep(session, Capture(session), name, "01-url-access-error-blurb");
        Assert.Equal(0, UiCmdLineCount(session.Relay!, AddIdentityLine));
        Assert.Equal(0, IdentityRowCount(session));
    }

    [Fact(Timeout = 120000)]
    public async Task SameNameTwiceSecondFails()
    {
        string name = nameof(SameNameTwiceSecondFails);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        EnterControllerUrl(session, Quickstart.UiControllerUrl);
        JoinEnrolledToNone(session);
        SaveStep(session, name, "01-identity-needs-ext-login");
        await AssertSameNameRejected(session, name, "02-add-failure-blurb");
    }
}
