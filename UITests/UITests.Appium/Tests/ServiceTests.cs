using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>The identity-details service list on landing-status.json's enabled-id.</summary>
[TestLifecycleLog]
[Trait("Category", "IdentityDetailServices")]
public class ServiceTests
{
    private static async Task<AppiumSession> LaunchToDetails(string name)
    {
        AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitForId(s, "ConnectLabel");
        OpenIdentityDetails(s, "enabled-id");
        WaitFor(s, By.XPath("//*[@Name='wiki.example']"));
        return s;
    }

    [Fact(Timeout = 60000)]
    public async Task Services_ClickDetailIcon_OpensServicePanel()
    {
        string name = nameof(Services_ClickDetailIcon_OpensServicePanel);
        await using AppiumSession s = await LaunchToDetails(name);
        ClickAt(s, WaitFor(s, By.XPath("//Image[@AutomationId='DetailIcon']")));
        WaitForId(s, "DetailName");
        SaveStep(s, name, "01-service-panel");
    }

    [Fact(Timeout = 60000)]
    public async Task Services_FilterNarrowsList()
    {
        string name = nameof(Services_FilterNarrowsList);
        await using AppiumSession s = await LaunchToDetails(name);
        // The filter has to have something to hide, or the wait below proves nothing.
        WaitFor(s, By.XPath("//*[@Name='prometheus.example']"));

        WaitFor(s, By.XPath("//*[@AutomationId='FilterServices']//Edit")).SendKeys("wiki");
        WaitForGone(s, By.XPath("//*[@Name='prometheus.example']"));
        WaitFor(s, By.XPath("//*[@Name='wiki.example']"));
        SaveStep(s, name, "01-after-typing-wiki");
    }
}
