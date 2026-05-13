using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Single comprehensive sort-headers walkthrough against the 15-identity
/// SortableMixed fixture. Clicks Status x2, Name x2, Services x2 and asserts
/// the active-arrow + ordering behaviour at each step.
///
/// Performance notes:
///   - Sort header clicks don't animate (no fade / slide); they just re-render
///     the list. 150ms settle is plenty.
///   - SaveStep is ~700ms a pop. Capture only the 4 most visually distinct
///     states, not every click.
///   - PageSource for a 15-row tree is the dominant cost. Fetch once per assert
///     block, never twice for the same UI state.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Sort")]
public class SortTests
{
    // Sort header clicks don't animate -- the list just re-renders synchronously
    // on the WPF dispatcher thread. WinAppDriver's element.Click() round-trip
    // (~200-500ms) is already plenty for the UIA tree to repopulate; no extra
    // settle wait needed.
    private const int SortSettleMs = 0;

    private static List<string> NamesFromSource(string src)
    {
        var names = new List<string>();
        var idFirst = new Regex("AutomationId=\"IdName\"[^>]*?\\bName=\"([^\"]+)\"");
        var nameFirst = new Regex("\\bName=\"([^\"]+)\"[^>]*?AutomationId=\"IdName\"");
        foreach (Match m in idFirst.Matches(src)) names.Add(m.Groups[1].Value);
        if (names.Count == 0)
        {
            foreach (Match m in nameFirst.Matches(src)) names.Add(m.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// Returns the active sort column + its arrow text. Only one of the three
    /// SortByXArrow TextBlocks is Visible at a time; the others are Collapsed
    /// and not in the UIA tree.
    /// </summary>
    // Single WinAppDriver round-trip via union XPath + FindElements (no throw).
    // The previous 3-probe-with-try/catch version cost 600-2000ms per call
    // because NoSuchElementException unwinds through the HTTP boundary.
    private static (string column, string arrow) ActiveSortArrow(AppiumSession s)
    {
        var arrows = s.Driver.FindElements(By.XPath(
            "//*[@AutomationId='SortByStatusArrow'] | " +
            "//*[@AutomationId='SortByNameArrow'] | " +
            "//*[@AutomationId='SortByServicesArrow']"));
        if (arrows.Count == 0) return ("", "");
        var el = arrows[0];
        var id = el.GetAttribute("AutomationId") ?? "";
        var col = id switch
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

    [Fact(Timeout = 20000)]
    public async Task Sort_FullWalkthrough_StatusNameServicesEachClickedTwice()
    {
        Trace.Begin();
        var name = nameof(Sort_FullWalkthrough_StatusNameServicesEachClickedTwice);

        await using var s = await Trace.TimeAsync("AppiumSession.LaunchAsync",
            () => AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed()));
        WaitForId(s, "ConnectLabel");
        await Trace.Settle(200);
        SaveStep(s, name, "01-landing-persisted-sort");

        // Cache the three sort header elements ONCE. ById walks the UIA tree
        // (~700ms each); resolving 8 separate clicks across 3 elements would
        // cost ~5.6s of duplicate lookups.
        var statusHdr   = Trace.Time("ById(SortByStatus) [cached]",   () => ById(s, "SortByStatus"));
        var nameHdr     = Trace.Time("ById(SortByName) [cached]",     () => ById(s, "SortByName"));
        var servicesHdr = Trace.Time("ById(SortByServices) [cached]", () => ById(s, "SortByServices"));

        // --- STATUS x2: only probe arrow after the SECOND click ---------------
        Trace.Time("click Status #1", () => statusHdr.Click());
        Trace.Time("click Status #2", () => statusHdr.Click());
        var (col2, arr2) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Status x2 -> {col2}/{arr2}");
        Assert.Equal("Status", col2);
        Assert.True(arr2 == "▲" || arr2 == "▼");
        SaveStep(s, name, "02-after-status-x2");

        // --- SERVICES x2: switches column then flips direction --------------
        Trace.Time("click Services #1", () => servicesHdr.Click());
        Trace.Time("click Services #2", () => servicesHdr.Click());
        var (col4, arr4) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Services x2 -> {col4}/{arr4}");
        Assert.Equal("Services", col4);
        Assert.Equal("▲", arr4);
        SaveStep(s, name, "03-after-services-x2");

        // --- NAME x2: switches column (-> Descending), then flips to Ascending
        Trace.Time("click Name #1", () => nameHdr.Click());
        Trace.Time("click Name #2", () => nameHdr.Click());
        var (col6, arr6) = ActiveSortArrow(s);
        Trace.Mark($"  arrow after Name x2 -> {col6}/{arr6}");
        Assert.Equal("Name", col6);
        Assert.Equal("▲", arr6);
        SaveStep(s, name, "04-after-name-x2-ascending");

        // We're now in Name Ascending; verify case-insensitive ordering on the
        // SAME PageSource (avoid a second fetch later).
        var order = Trace.Time("PageSource + NamesFromSource",
            () => NamesFromSource(s.Driver.PageSource));
        Assert.True(order.Count >= 5, $"Expected >=5 rows, got {order.Count}");
        var alpha = IndexOf(order, "ALPHA-DEV");
        var bravo = IndexOf(order, "Bravo-Staging");
        var charlie = IndexOf(order, "CharlieEdge");
        var oscar = IndexOf(order, "oscar-prod");
        var zebra = IndexOf(order, "zebra-prod");
        Assert.True(alpha < bravo && bravo < charlie && charlie < oscar && oscar < zebra,
            $"Ascending Name case-insensitive: expected ALPHA<Bravo<Charlie<oscar<zebra, got {alpha}/{bravo}/{charlie}/{oscar}/{zebra} in [{string.Join(", ", order)}]");
    }
}
