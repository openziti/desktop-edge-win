using Newtonsoft.Json.Linq;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class IdentityOnOffTests
{
    private readonly IntegrationFixture _fixture;

    public IdentityOnOffTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task TogglesActiveState()
    {
        string name = nameof(TogglesActiveState);
        const string identityName = "test_on_off";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        WaitFor(s, ToggleStatus(identityName, "ENABLED"));
        await VerifyStep(Capture(s), name, "01-added-enabled");

        ToggleOff(s, identityName);
        WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        await VerifyStep(Capture(s), name, "02-toggled-off");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, OffLine)["Code"]);
        WaitForZetEventAfter(s, OffLine, ControllerDisconnectedEvent);

        ToggleOn(s, identityName);
        WaitForController(s, ToggleStatus(identityName, "ENABLED"), $"{identityName} shows ENABLED");
        await VerifyStep(Capture(s), name, "03-toggled-on");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, OnLine)["Code"]);
        JObject on = WaitForZetEventAfter(s, OnLine, IdentityAddedEvent);
        Assert.True((bool?)on["Id"]!["Active"] == true, $"ZET's identity added event is not active after on: {on}");
        WaitForZetEventAfter(s, OnLine, ControllerConnectedEvent);
    }
}
