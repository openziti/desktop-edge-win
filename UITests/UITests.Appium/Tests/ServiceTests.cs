using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tests that exercise the identity-details service list: rendering of all
/// services, the per-service detail icon, the filter input, and the
/// ForgetIdentityButton.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceTests
{
    [Fact(Timeout = 120000)]
    public async Task Services_DetailListShowsAllThreeServices()
    {
        var name = nameof(Services_DetailListShowsAllThreeServices);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details");

        // Wait for at least one service row to render before snapshotting PageSource.
        WaitFor(s, By.XPath("//*[@Name='wiki.example']"));
        var src = s.Driver.PageSource;
        Assert.Contains("wiki.example", src);
        Assert.Contains("prometheus.example", src);
        Assert.Contains("bastion.example", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Services_ClickDetailIcon_OpensServicePanel()
    {
        var name = nameof(Services_ClickDetailIcon_OpensServicePanel);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details");

        // Each service row exposes a DetailIcon Image. Find the first and synthesize
        // a click via Actions -- Image elements often aren't pointer-interactable.
        var icons = s.Driver.FindElements(By.XPath("//Image[@AutomationId='DetailIcon']"));
        Assert.True(icons.Count > 0, "expected at least one DetailIcon image");
        ClickAt(s, icons[0]);
        await Task.Delay(500);
        SaveStep(s, name, "03-after-detail-icon-click");

        // DetailsArea contains DetailName/DetailUrl/DetailAddress text boxes. Assert
        // at least one of those is now in the UIA tree.
        var detail = s.Driver.FindElements(By.XPath("//*[@AutomationId='DetailName']")).Count
                   + s.Driver.FindElements(By.XPath("//*[@AutomationId='DetailUrl']")).Count
                   + s.Driver.FindElements(By.XPath("//*[@AutomationId='DetailAddress']")).Count;
        Assert.True(detail > 0, "expected DetailName/DetailUrl/DetailAddress to appear after clicking DetailIcon");
    }

    [Fact(Timeout = 120000)]
    public async Task Services_FilterNarrowsList()
    {
        var name = nameof(Services_FilterNarrowsList);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details");

        // Confirm all 3 are present pre-filter.
        WaitFor(s, By.XPath("//*[@Name='wiki.example']"));
        Assert.Contains("prometheus.example", s.Driver.PageSource);

        // FilterServices is a Custom control; its internal TextBox surfaces as UIA
        // control type Edit. Probe a few plausible locators.
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
            var found = s.Driver.FindElements(by);
            if (found.Count > 0) { filterInput = found[0]; break; }
        }
        Assert.NotNull(filterInput);

        filterInput!.SendKeys("wiki");
        await Task.Delay(500); // debounce / filter pass
        SaveStep(s, name, "03-after-typing-wiki");

        var src = s.Driver.PageSource;
        Assert.Contains("wiki.example", src);
        Assert.DoesNotContain("prometheus.example", src);
    }

    [Fact(Timeout = 120000)]
    public async Task Services_ForgetIdentityButton_IsRendered()
    {
        var name = nameof(Services_ForgetIdentityButton_IsRendered);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixturesDir());
        WaitForId(s, "ConnectLabel");
        SaveStep(s, name, "01-landing");

        OpenIdentityDetails(s, "enabled-id");
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details");

        Assert.Contains("Forget", s.Driver.PageSource);
    }

    [Fact(Timeout = 120000)]
    public async Task Services_AlternateFixtureShowsDifferentNames()
    {
        var name = nameof(Services_AlternateFixtureShowsDifferentNames);
        await using var s = await AppiumSession.LaunchAsync(
            DefaultExePath(), FixturesDir(), fixtureFile: "with-services.json");
        WaitForId(s, "ConnectLabel");
        WaitFor(s, By.XPath("//Text[@Name='with-3-services-id']"));
        SaveStep(s, name, "01-landing-with-services-fixture");

        OpenIdentityDetails(s, "with-3-services-id");
        await Task.Delay(600);
        SaveStep(s, name, "02-identity-details");

        // Wait for at least one of the three to render, then snapshot PageSource.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var src = s.Driver.PageSource;
            if (src.Contains("jenkins.example") || src.Contains("grafana.example") || src.Contains("postgres.example"))
                break;
            await Task.Delay(150);
        }

        var page = s.Driver.PageSource;
        Assert.Contains("jenkins.example", page);
        Assert.Contains("grafana.example", page);
        Assert.Contains("postgres.example", page);
    }
}
