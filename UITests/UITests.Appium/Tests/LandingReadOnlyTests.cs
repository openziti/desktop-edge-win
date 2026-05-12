using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Read-only assertions against the default landing screen. All tests in this
/// class share a single UI launch via LandingSession.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "MainScreen")]
public class LandingReadOnlyTests : IClassFixture<LandingSession>
{
    private readonly LandingSession _f;
    public LandingReadOnlyTests(LandingSession f) => _f = f;
    private AppiumSession S => _f.Session;

    [Fact(Timeout = 60000)]
    public async Task Elements_ResolveByAccessibilityId()
    {
        Assert.True(ById(S, "ConnectLabel").Displayed);
        Assert.True(ById(S, "SortByStatus").Displayed);
        Assert.True(ById(S, "IdListScroller").Displayed);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task IdentityList_ShowsBothMockIdentities()
    {
        Assert.True(WaitFor(S, By.XPath("//Text[@Name='enabled-id']")).Displayed);
        Assert.True(WaitFor(S, By.XPath("//Text[@Name='disabled-at-start-id']")).Displayed);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task IdentityList_ReflectsActiveAndInactiveStatus()
    {
        // Find by name -- UI sorts identities alphabetically so order is not predictable
        // from the fixture order.
        var enabledRow  = IdentityRow(S, "enabled-id");
        var disabledRow = IdentityRow(S, "disabled-at-start-id");

        Assert.Equal("ENABLED",
            enabledRow.FindElement(By.XPath(".//*[@AutomationId='ToggleStatus']")).Text);
        Assert.Equal("DISABLED",
            disabledRow.FindElement(By.XPath(".//*[@AutomationId='ToggleStatus']")).Text);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task SortHeaders_AllVisible()
    {
        Assert.Equal("Status", ById(S, "SortByStatus").Text);
        Assert.Equal("Name", ById(S, "SortByName").Text);
        Assert.Equal("Services", ById(S, "SortByServices").Text);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task ConnectedTime_IsDisplayed()
    {
        var time = ById(S, "ConnectedTime").Text;
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", time);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task ServiceCount_ShowsThreeForEnabled_DashForDisabled()
    {
        var enabledRow  = IdentityRow(S, "enabled-id");
        var disabledRow = IdentityRow(S, "disabled-at-start-id");

        Assert.Equal("3",
            enabledRow.FindElement(By.XPath(".//*[@AutomationId='ServiceCount']")).Text);
        Assert.Equal("-",
            disabledRow.FindElement(By.XPath(".//*[@AutomationId='ServiceCount']")).Text);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task ConnectLabel_ReadsTapToDisconnect_WhenActive()
    {
        Assert.Equal("Tap to Disconnect", ById(S, "ConnectLabel").Text);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 60000)]
    public async Task DumpPageSource()
    {
        var src = S.Driver.PageSource;
        var path = Path.Combine(RepoRoot(), "UITests", "page-source.xml");
        File.WriteAllText(path, src);
        Assert.True(src.Length > 0);
        await Task.CompletedTask;
    }
}
