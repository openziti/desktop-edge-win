using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of ziti-tunnel-sdk-c tests/integration/identity_on_off_test.go.</summary>
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
        Trace.Begin();
        string name = nameof(TogglesActiveState);
        const string identityName = "test_on_off";

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        WaitFor(s, ToggleStatus(identityName, "ENABLED"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-added-enabled");
        await VerifyScreen(Capture(s), "added-enabled");

        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        WaitForController(s, ToggleStatus(identityName, "DISABLED"), $"{identityName} shows DISABLED");
        await Trace.Settle(350);
        SaveStep(s, name, "02-toggled-off");
        await VerifyScreen(Capture(s), "toggled-off");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, OffLine)["Code"]);
        WaitForZetEventAfter(s, OffLine, "\"Op\":\"controller\",\"Action\":\"disconnected\"");

        ClickAt(s, WaitFor(s, InIdentityRow(identityName, "ToggleSwitch")));
        WaitForController(s, ToggleStatus(identityName, "ENABLED"), $"{identityName} shows ENABLED");
        await Trace.Settle(350);
        SaveStep(s, name, "03-toggled-on");
        await VerifyScreen(Capture(s), "toggled-on");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, OnLine)["Code"]);
        JObject on = WaitForZetEventAfter(s, OnLine, "\"Op\":\"identity\",\"Action\":\"added\"");
        Assert.True((bool?)on["Id"]!["Active"] == true, $"ZET's identity added event is not active after on: {on}");
        WaitForZetEventAfter(s, OnLine, "\"Op\":\"controller\",\"Action\":\"connected\"");
    }

    private const string OffLine = "\"OnOff\":false";
    private const string OnLine = "\"OnOff\":true";
}
