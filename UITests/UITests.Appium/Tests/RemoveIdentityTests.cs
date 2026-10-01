using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>UI twin of ziti-tunnel-sdk-c tests/integration/remove_identity_test.go.</summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class RemoveIdentityTests
{
    private readonly IntegrationFixture _fixture;

    public RemoveIdentityTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task WithIdentifierFromEvent()
    {
        Trace.Begin();
        string name = nameof(WithIdentifierFromEvent);
        const string identityName = "test_remove_id";
        string identityFile = AddedIdentityFile(_fixture);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        await Trace.Settle(350);
        SaveStep(s, name, "01-identity-added");
        await VerifyScreen(Capture(s), "identity-added");

        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='ForgetIdentityButton']")));
        await Trace.Settle(300);
        SaveStep(s, name, "02-forget-confirm");
        await VerifyScreen(Capture(s), "forget-confirm");

        ClickAt(s, WaitFor(s, By.XPath("//*[@AutomationId='ConfirmButton']")));
        WaitForGone(s, By.XPath($"//Text[@Name='{identityName}']"));
        await Trace.Settle(300);
        SaveStep(s, name, "03-identity-forgotten");
        await VerifyScreen(Capture(s), "identity-forgotten");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, "\"Command\":\"RemoveIdentity\"")["Code"]);
        Assert.Equal(0, IdentityRowCount(s));
        Assert.False(File.Exists(identityFile), $"identity file should be removed after forget: {identityFile}");
    }
}
