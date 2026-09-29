using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Clicks the Status, Services and Name headers twice each on the SortableMixed fixture, checking the active arrow
/// and, at the end, the name order. SaveStep costs about 700ms and PageSource dominates, so it captures four states
/// and fetches the tree once per check.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Sort")]
public class SortTests
{
    private static List<string> NamesFromSource(string src)
    {
        List<string> names = new List<string>();
        // WinAppDriver's page source writes AutomationId before Name.
        Regex idThenName = new Regex("AutomationId=\"IdName\"[^>]*?\\bName=\"([^\"]+)\"");
        foreach (Match m in idThenName.Matches(src)) names.Add(m.Groups[1].Value);
        return names;
    }

    /// <summary>
    /// The active sort column and its arrow. Only one SortByXArrow is visible at a time, and collapsed ones are not in
    /// the tree, so one union XPath finds it in a single round trip.
    /// </summary>
    private static (string column, string arrow) ActiveSortArrow(AppiumSession s)
    {
        System.Collections.ObjectModel.ReadOnlyCollection<AppiumElement> arrows = s.Driver.FindElements(By.XPath(
            "//*[@AutomationId='SortByStatusArrow'] | " +
            "//*[@AutomationId='SortByNameArrow'] | " +
            "//*[@AutomationId='SortByServicesArrow']"));
        if (arrows.Count == 0) return ("", "");
        AppiumElement el = arrows[0];
        string id = el.GetAttribute("AutomationId") ?? "";
        string col = id switch
        {
            "SortByStatusArrow" => "Status",
            "SortByNameArrow" => "Name",
            "SortByServicesArrow" => "Services",
            _ => "",
        };
        return (col, el.Text ?? "");
    }

    private static int IndexOf(List<string> order, string needle) =>
        order.FindIndex(n => n.Equals(needle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every name in first is listed, and all of them come before every name in rest.</summary>
    private static void AssertGroupedFirst(List<string> order, string[] first, string[] rest)
    {
        int lastOfFirst = -1;
        foreach (string needle in first)
        {
            int index = IndexOf(order, needle);
            Assert.True(index >= 0, $"Expected '{needle}' in [{string.Join(", ", order)}]");
            lastOfFirst = Math.Max(lastOfFirst, index);
        }
        foreach (string needle in rest)
        {
            int index = IndexOf(order, needle);
            Assert.True(index > lastOfFirst,
                $"Expected [{string.Join(", ", first)}] before '{needle}' in [{string.Join(", ", order)}]");
        }
    }

    // Enabled state in SortableMixed.
    private static readonly string[] Disabled = { "Bravo-Staging", "CharlieEdge" };
    private static readonly string[] Enabled = { "zebra-prod", "ALPHA-DEV", "oscar-prod" };

    [Fact(Timeout = 40000)]
    public async Task SortHeadersReorderIdentities()
    {
        Trace.Begin();
        string name = nameof(SortHeadersReorderIdentities);

        await using AppiumSession s = await Trace.TimeAsync("AppiumSession.LaunchAsync",
            () => AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed()));
        WaitForId(s, "ConnectLabel");
        await Trace.Settle(200);
        SaveStep(s, name, "01-landing-persisted-sort");
        // The sort persists in the user's user.config between runs, so the Status result depends on where it starts.
        (string startColumn, string startArrow) = ActiveSortArrow(s);

        // Resolved once: each ById walks the tree for about 700ms.
        IWebElement statusHdr = Trace.Time("ById(SortByStatus) [cached]",   () => ById(s, "SortByStatus"));
        IWebElement nameHdr = Trace.Time("ById(SortByName) [cached]",     () => ById(s, "SortByName"));
        IWebElement servicesHdr = Trace.Time("ById(SortByServices) [cached]", () => ById(s, "SortByServices"));

        // Status twice, arrow checked only after the second click.
        Trace.Time("click Status #1", () => statusHdr.Click());
        Trace.Time("click Status #2", () => statusHdr.Click());
        (string col2, string arr2) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Status x2 -> {col2}/{arr2}");
        Assert.Equal("Status", col2);
        // A new column sorts descending and a second click flips it (MainViewModel.SetSort). Already on Status, the
        // two clicks flip twice and land back on the starting direction.
        Assert.Equal(startColumn == "Status" ? startArrow : "▲", arr2);
        SaveStep(s, name, "02-after-status-x2");
        // Status sorts by enabled state, and ascending (▲) puts disabled identities first.
        List<string> statusOrder = NamesFromSource(s.Driver.PageSource);
        if (arr2 == "▲") AssertGroupedFirst(statusOrder, Disabled, Enabled);
        else AssertGroupedFirst(statusOrder, Enabled, Disabled);

        // Services twice: the first click switches column, the second flips direction.
        Trace.Time("click Services #1", () => servicesHdr.Click());
        Trace.Time("click Services #2", () => servicesHdr.Click());
        (string col4, string arr4) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Services x2 -> {col4}/{arr4}");
        Assert.Equal("Services", col4);
        Assert.Equal("▲", arr4);
        SaveStep(s, name, "03-after-services-x2");
        // Services sorts by auth state before service count, and no fixture identity has services. Ascending puts
        // CharlieEdge, the only one needing external auth, ahead of the rest.
        AssertGroupedFirst(NamesFromSource(s.Driver.PageSource),
            new[] { "CharlieEdge" }, new[] { "zebra-prod", "Bravo-Staging", "ALPHA-DEV", "oscar-prod" });

        // Name twice: the first click switches column to descending, the second flips to ascending.
        Trace.Time("click Name #1", () => nameHdr.Click());
        Trace.Time("click Name #2", () => nameHdr.Click());
        (string col6, string arr6) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Name x2 -> {col6}/{arr6}");
        Assert.Equal("Name", col6);
        Assert.Equal("▲", arr6);
        SaveStep(s, name, "04-after-name-x2-ascending");

        // Name ascending must ignore case.
        List<string> order = Trace.Time("PageSource + NamesFromSource",
            () => NamesFromSource(s.Driver.PageSource));
        Assert.True(order.Count >= 5, $"Expected >=5 rows, got {order.Count}");
        int alpha = IndexOf(order, "ALPHA-DEV");
        int bravo = IndexOf(order, "Bravo-Staging");
        int charlie = IndexOf(order, "CharlieEdge");
        int oscar = IndexOf(order, "oscar-prod");
        int zebra = IndexOf(order, "zebra-prod");
        Assert.True(alpha >= 0 && bravo >= 0 && charlie >= 0 && oscar >= 0 && zebra >= 0,
            $"Expected every fixture name in the list, got [{string.Join(", ", order)}]");
        Assert.True(alpha < bravo && bravo < charlie && charlie < oscar && oscar < zebra,
            $"Ascending Name case-insensitive: expected ALPHA<Bravo<Charlie<oscar<zebra, got {alpha}/{bravo}/{charlie}/{oscar}/{zebra} in [{string.Join(", ", order)}]");
    }
}
