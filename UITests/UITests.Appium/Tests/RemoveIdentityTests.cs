using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class RemoveIdentityTests
{
    private readonly IntegrationFixture _fixture;

    public RemoveIdentityTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 120000)]
    public async Task WithIdentifierFromEvent()
    {
        string name = nameof(WithIdentifierFromEvent);
        const string identityName = "test_remove_id";
        string identityFile = AddedIdentityFile(_fixture);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        AddIdentity(_fixture, session, identityName);
        await VerifyStep(session, Capture(session), name, "01-identity-added");

        OpenIdentityDetails(session, identityName);
        ClickAt(session, WaitFor(session, ById("ForgetIdentityButton")));
        await VerifyStep(session, Capture(session), name, "02-forget-confirm");

        ClickAt(session, WaitFor(session, ById("ConfirmButton")));
        WaitForGone(session, By.XPath($"//Text[@Name='{identityName}']"));
        await VerifyStep(session, Capture(session), name, "03-identity-forgotten");
        Assert.Equal(0, (int?)WaitForZetReplyTo(session, "\"Command\":\"RemoveIdentity\"")["Code"]);
        Assert.Equal(0, IdentityRowCount(session));
        Assert.False(File.Exists(identityFile), $"identity file should be removed after forget: {identityFile}");
    }
}
