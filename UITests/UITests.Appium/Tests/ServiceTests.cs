using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tests that exercise the identity-details service list. Four of these tests
/// use the default fixture + enabled-id and share ONE AppiumSession via
/// IClassFixture (saving ~6-8s of launch overhead). Each test starts by
/// re-opening details from landing and ends by closing details so the next
/// test starts clean. The alternate-fixture test owns its own session because
/// the fixture is baked into the mock at launch.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceTests : IClassFixture<LandingSession>, IAsyncLifetime
{
    private readonly LandingSession _f;
    private AppiumSession S => _f.Session;

    public ServiceTests(LandingSession f) { _f = f; }

    /// <summary>
    /// Each shared-session test starts here. DisposeAsync of the prior test
    /// already closed details, so we just open them directly. OpenIdentityDetails
    /// calls IdentityRow which does its own WaitFor on the enabled-id row, so
    /// we don't need to pre-wait either.
    /// </summary>
    public async Task InitializeAsync()
    {
        OpenIdentityDetails(S, "enabled-id");

        // Reset the FilterServices textbox to empty if a prior test left text
        // in it. IWebElement.Clear() doesn't reliably fire the WPF TextChanged
        // binding, so we use Ctrl+A + Delete.
        var filterInputs = S.Driver.FindElements(By.XPath("//*[@AutomationId='FilterServices']//Edit"));
        if (filterInputs.Count > 0 && !string.IsNullOrEmpty(filterInputs[0].Text))
        {
            filterInputs[0].SendKeys(Keys.Control + "a" + Keys.Control);
            filterInputs[0].SendKeys(Keys.Delete);
            await Trace.Settle(150);
        }
    }

    /// <summary>Reset after each shared-session test so the next test starts clean.</summary>
    public Task DisposeAsync()
    {
        try { CloseIdentityDetails(S); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    [Fact(Timeout = 10000)]
    public async Task Services_DetailListShowsAllThreeServices()
    {
        var name = nameof(Services_DetailListShowsAllThreeServices);
        SaveStep(S, name, "01-identity-details");

        WaitFor(S, By.XPath("//*[@Name='wiki.example']"));
        var src = S.Driver.PageSource;
        Assert.Contains("wiki.example", src);
        Assert.Contains("prometheus.example", src);
        Assert.Contains("bastion.example", src);
        await Task.CompletedTask;
    }

    [Fact(Timeout = 10000)]
    public async Task Services_ClickDetailIcon_OpensServicePanel()
    {
        var name = nameof(Services_ClickDetailIcon_OpensServicePanel);
        SaveStep(S, name, "01-identity-details");

        var icons = S.Driver.FindElements(By.XPath("//Image[@AutomationId='DetailIcon']"));
        Assert.True(icons.Count > 0, "expected at least one DetailIcon image");
        ClickAt(S, icons[0]);
        await Trace.Settle(350);
        SaveStep(S, name, "02-after-detail-icon-click");

        var detail = S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailName']")).Count
                   + S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailUrl']")).Count
                   + S.Driver.FindElements(By.XPath("//*[@AutomationId='DetailAddress']")).Count;
        Assert.True(detail > 0, "expected DetailName/DetailUrl/DetailAddress to appear after clicking DetailIcon");
    }

    [Fact(Timeout = 10000)]
    public async Task Services_FilterNarrowsList()
    {
        var name = nameof(Services_FilterNarrowsList);

        WaitFor(S, By.XPath("//*[@Name='wiki.example']"));
        Assert.Contains("prometheus.example", S.Driver.PageSource);

        IWebElement? filterInput = null;
        var locators = new[]
        {
            By.XPath("//*[@AutomationId='FilterServices']//Edit"),
            By.XPath("//*[@AutomationId='FilterServices']//*[@ClassName='TextBox']"),
            By.XPath("//Custom[@AutomationId='FilterServices']//Edit"),
            By.XPath("//*[@AutomationId='FilterServices']/descendant::Edit[1]"),
        };
        foreach (var by in locators)
        {
            var found = S.Driver.FindElements(by);
            if (found.Count > 0) { filterInput = found[0]; break; }
        }
        Assert.NotNull(filterInput);

        filterInput!.SendKeys("wiki");

        var deadline = DateTime.UtcNow.AddSeconds(3);
        string src = "";
        while (DateTime.UtcNow < deadline)
        {
            src = S.Driver.PageSource;
            if (!src.Contains("prometheus.example")) break;
            await Task.Delay(100);
        }
        SaveStep(S, name, "01-after-typing-wiki");

        Assert.Contains("wiki.example", src);
        Assert.DoesNotContain("prometheus.example", src);

        // Clear the filter so the next test (DisposeAsync -> InitializeAsync)
        // doesn't inherit "wiki" in the textbox after re-opening details.
        filterInput.Clear();
    }

    [Fact(Timeout = 10000)]
    public async Task Services_ForgetIdentityButton_IsRendered()
    {
        var name = nameof(Services_ForgetIdentityButton_IsRendered);
        SaveStep(S, name, "01-identity-details");
        Assert.Contains("Forget", S.Driver.PageSource);
        await Task.CompletedTask;
    }

}

/// <summary>
/// Lives in its own class because it loads an alternate fixture
/// (with-services.json) and so cannot share the LandingSession used by
/// ServiceTests. Lives next to ServiceTests to keep all Services_* together
/// for `dotnet test --filter FullyQualifiedName~Services_`.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceAltFixtureTests
{
    [Fact(Timeout = 15000)]
    public async Task Services_AlternateFixtureShowsDifferentNames()
    {
        var name = nameof(Services_AlternateFixtureShowsDifferentNames);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "with-services.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='with-3-services-id']"));
        SaveStep(s, name, "01-landing-with-services-fixture");

        OpenIdentityDetails(s, "with-3-services-id");
        await Trace.Settle(350);
        SaveStep(s, name, "02-identity-details");

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var src = s.Driver.PageSource;
            if (src.Contains("jenkins.example") || src.Contains("grafana.example") || src.Contains("postgres.example"))
                break;
            await Task.Delay(250);
        }

        var page = s.Driver.PageSource;
        Assert.Contains("jenkins.example", page);
        Assert.Contains("grafana.example", page);
        Assert.Contains("postgres.example", page);
    }
}
