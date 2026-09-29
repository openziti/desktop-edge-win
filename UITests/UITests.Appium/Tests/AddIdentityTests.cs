using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of TestAddIdentityByJwt in ziti-tunnel-sdk-c tests/integration/add_identity_test.go.</summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class AddIdentityTests
{
    private static readonly By JwtInvalidError = By.XPath("//*[@AutomationId='ErrorTitle' and @Name='JWT Invalid']");
    private static readonly By AddFailureBlurb =
        By.XPath("//*[@AutomationId='Blurb' and @Name='Unexpected error when adding identity!']");

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
        WaitForController(s, AddFailureBlurb, "the add failure blurb shows");
        await Trace.Settle(500);
        SaveStep(s, name, "02-add-failure-blurb");
        await VerifyScreen(Capture(s), "add-failure-blurb");
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
        WaitForController(s, AddFailureBlurb, "the add failure blurb shows");
        await Trace.Settle(500);
        SaveStep(s, name, "01-add-failure-blurb");
        await VerifyScreen(Capture(s), "add-failure-blurb");
        JObject reply = ZetReplyTo(s.Relay!, "\"Command\":\"AddIdentity\"");
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Contains("JWT not accepted by controller", (string?)reply["Error"]);
        Assert.Equal(0, IdentityRowCount(s));
    }
}
