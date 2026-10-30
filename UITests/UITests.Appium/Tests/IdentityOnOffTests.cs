using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class IdentityOnOffTests
{
    private readonly IntegrationFixture _fixture;

    public IdentityOnOffTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task TogglesActiveState()
    {
        string name = nameof(TogglesActiveState);
        const string identityName = "test_on_off";

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        WaitFor(session, ToggleStatus(identityName, "ENABLED"));
        await VerifyStep(session, Capture(session), name, "01-added-enabled");

        ToggleOff(session, identityName);
        WaitForController(session, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        await VerifyStep(session, Capture(session), name, "02-toggled-off");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, OffLine)["Code"]);
        WaitForZetEventAfter(session, OffLine, ControllerDisconnectedEvent);

        ToggleOn(session, identityName);
        WaitForController(session, ToggleStatus(identityName, "ENABLED"), $"{identityName} shows ENABLED");
        await VerifyStep(session, Capture(session), name, "03-toggled-on");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, OnLine)["Code"]);
        JObject identityAddedEvent = WaitForZetEventAfter(session, OnLine, IdentityAddedEvent);
        Assert.True((bool?)identityAddedEvent["Id"]!["Active"] == true, $"ZET's identity added event is not active after on: {identityAddedEvent}");
        WaitForZetEventAfter(session, OnLine, ControllerConnectedEvent);
    }
}
