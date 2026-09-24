using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// The identity-details service list on enabled-id, sharing one session. Each test opens details and closes them
/// again so the next starts clean.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceTests : IClassFixture<LandingSession>, IAsyncLifetime
{
    private readonly LandingSession _f;
    private AppiumSession S => _f.Session;

    public ServiceTests(LandingSession f) { _f = f; }

    public async Task InitializeAsync()
    {
        OpenIdentityDetails(S, "enabled-id");

        // Clear filter text a prior test left behind. IWebElement.Clear() doesn't reliably fire WPF's TextChanged,
        // so select all and delete.
        System.Collections.ObjectModel.ReadOnlyCollection<AppiumElement> filterInputs = S.Driver.FindElements(By.XPath("//*[@AutomationId='FilterServices']//Edit"));
        if (filterInputs.Count > 0 && !string.IsNullOrEmpty(filterInputs[0].Text))
        {
            filterInputs[0].SendKeys(Keys.Control + "a" + Keys.Control);
            filterInputs[0].SendKeys(Keys.Delete);
            await Trace.Settle(150);
        }
    }

    public Task DisposeAsync()
    {
        CloseIdentityDetails(S);
        return Task.CompletedTask;
    }

    [Fact(Timeout = 20000)]
    public async Task Services_DetailListShowsAllThreeServices()
    {
        string name = nameof(Services_DetailListShowsAllThreeServices);
        SaveStep(S, name, "01-identity-details");

        WaitFor(S, By.XPath("//*[@Name='wiki.example']"));
        string src = S.Driver.PageSource;
        Assert.Contains("wiki.example", src);
        Assert.Contains("prometheus.example", src);
        Assert.Contains("bastion.example", src);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 20000)]
    public async Task Services_ClickDetailIcon_OpensServicePanel()
    {
        string name = nameof(Services_ClickDetailIcon_OpensServicePanel);
        SaveStep(S, name, "01-identity-details");

        System.Collections.ObjectModel.ReadOnlyCollection<AppiumElement> icons = S.Driver.FindElements(By.XPath("//Image[@AutomationId='DetailIcon']"));
        Assert.True(icons.Count > 0, "expected at least one DetailIcon image");
        ClickAt(S, icons[0]);
        await Trace.Settle(350);
        SaveStep(S, name, "02-after-detail-icon-click");

        int detail = S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailName']")).Count
                   + S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailUrl']")).Count
                   + S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailAddress']")).Count;
        Assert.True(detail > 0, "expected DetailName/DetailUrl/DetailAddress to appear after clicking DetailIcon");

        // The panel covers identity details, so DisposeAsync's close click can't reach the title X while it's open.
        ClickUntilGone(S, By.XPath("//*[@AutomationId='ServiceDetailsClose']"));
    }

    [Fact(Timeout = 20000)]
    public async Task Services_FilterNarrowsList()
    {
        string name = nameof(Services_FilterNarrowsList);

        WaitFor(S, By.XPath("//*[@Name='wiki.example']"));
        Assert.Contains("prometheus.example", S.Driver.PageSource);

        IWebElement filterInput = WaitFor(S, By.XPath("//*[@AutomationId='FilterServices']//Edit"));
        filterInput.SendKeys("wiki");

        string src = "";
        WaitUntil(S, "the wiki filter hides prometheus.example", TimeSpan.FromSeconds(3),
            () => !(src = S.Driver.PageSource).Contains("prometheus.example"));
        SaveStep(S, name, "01-after-typing-wiki");

        Assert.Contains("wiki.example", src);
        Assert.DoesNotContain("prometheus.example", src);

        // Best effort only: InitializeAsync clears it properly for the next test.
        filterInput.Clear();
        await Task.CompletedTask;
    }

    [Fact(Timeout = 20000)]
    public async Task Services_ForgetIdentityButton_IsRendered()
    {
        string name = nameof(Services_ForgetIdentityButton_IsRendered);
        SaveStep(S, name, "01-identity-details");
        WaitForId(S, "ForgetIdentityButton");
        await Task.CompletedTask;
    }

}

/// <summary>
/// Its own class because the with-services.json fixture is fixed at launch, so it can't share ServiceTests' session.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceAltFixtureTests
{
    [Fact(Timeout = 30000)]
    public async Task Services_AlternateFixtureShowsDifferentNames()
    {
        string name = nameof(Services_AlternateFixtureShowsDifferentNames);
        await using AppiumSession s = await AppiumSession.LaunchAsync(
            DefaultExePath(), Fixture("with-services.json"));
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='with-3-services-id']"));
        SaveStep(s, name, "01-landing-with-services-fixture");

        OpenIdentityDetails(s, "with-3-services-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        WaitUntil(s, "the with-services fixture's services show", TimeSpan.FromSeconds(8),
            () => s.Driver.PageSource.Contains("jenkins.example"));

        string page = s.Driver.PageSource;
        Assert.Contains("jenkins.example", page);
        Assert.Contains("grafana.example", page);
        Assert.Contains("postgres.example", page);
    }
}
