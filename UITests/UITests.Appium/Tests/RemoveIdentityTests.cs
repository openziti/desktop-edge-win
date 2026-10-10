using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

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
        string name = nameof(WithIdentifierFromEvent);
        const string identityName = "test_remove_id";
        string identityFile = AddedIdentityFile(_fixture);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, s, identityName);
        await VerifyStep(Capture(s), name, "01-identity-added");

        OpenIdentityDetails(s, identityName);
        ClickAt(s, WaitFor(s, ById("ForgetIdentityButton")));
        await VerifyStep(Capture(s), name, "02-forget-confirm");

        ClickAt(s, WaitFor(s, ById("ConfirmButton")));
        WaitForGone(s, By.XPath($"//Text[@Name='{identityName}']"));
        await VerifyStep(Capture(s), name, "03-identity-forgotten");
        Assert.Equal(0, (int?)ZetReplyTo(s.Relay!, "\"Command\":\"RemoveIdentity\"")["Code"]);
        Assert.Equal(0, IdentityRowCount(s));
        Assert.False(File.Exists(identityFile), $"identity file should be removed after forget: {identityFile}");
    }
}
