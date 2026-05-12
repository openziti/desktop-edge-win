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
    // Sort header clicks don't animate -- the list just re-renders. 50ms is
    // enough for the IdList children to repopulate before the next assertion.
    private const int SortSettleMs = 50;

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
    private static (string column, string arrow) ActiveSortArrow(AppiumSession s)
    {
        var status = TryGetTextById(s, "SortByStatusArrow");
        if (!string.IsNullOrEmpty(status)) return ("Status", status);
        var nameArrow = TryGetTextById(s, "SortByNameArrow");
        if (!string.IsNullOrEmpty(nameArrow)) return ("Name", nameArrow);
        var services = TryGetTextById(s, "SortByServicesArrow");
        if (!string.IsNullOrEmpty(services)) return ("Services", services);
        return ("", "");
    }

    private static int IndexOf(List<string> order, string needle) =>
        order.FindIndex(n => n.Equals(needle, StringComparison.OrdinalIgnoreCase));

    // 6 click cycles + 5 PageSource fetches against the SortableMixed fixture
    // is a comprehensive walkthrough; budget is the launch (~3s) plus ~5s of
    // UI interactions plus per-cycle PageSource cost.
    [Fact(Timeout = 30000)]
    public async Task Sort_FullWalkthrough_StatusNameServicesEachClickedTwice()
    {
        var name = nameof(Sort_FullWalkthrough_StatusNameServicesEachClickedTwice);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(300); // let all 15 rows render
        SaveStep(s, name, "01-landing-persisted-sort");

        // --- STATUS x2 -------------------------------------------------------
        ById(s, "SortByStatus").Click();
        await Task.Delay(SortSettleMs);
        var (col1, arr1) = ActiveSortArrow(s);
        Assert.Equal("Status", col1);
        Assert.True(arr1 == "▲" || arr1 == "▼");

        // ENABLED + DISABLED clusters (no interleaving) after Status sort.
        var src1 = s.Driver.PageSource;
        var enabled1 = Regex.Matches(src1, "Name=\"ENABLED\"").Select(m => m.Index).ToList();
        var disabled1 = Regex.Matches(src1, "Name=\"DISABLED\"").Select(m => m.Index).ToList();
        Assert.NotEmpty(enabled1);
        Assert.NotEmpty(disabled1);
        Assert.True(enabled1.Max() < disabled1.Min() || disabled1.Max() < enabled1.Min(),
            "ENABLED and DISABLED labels should cluster contiguously after Status sort.");

        ById(s, "SortByStatus").Click(); // 2nd click flips direction
        await Task.Delay(SortSettleMs);
        var (col2, arr2) = ActiveSortArrow(s);
        Assert.Equal("Status", col2);
        Assert.NotEqual(arr1, arr2);
        SaveStep(s, name, "02-after-status-x2");

        // --- NAME x2 ---------------------------------------------------------
        ById(s, "SortByName").Click(); // switches column, defaults to Descending
        await Task.Delay(SortSettleMs);
        var (col3, arr3) = ActiveSortArrow(s);
        Assert.Equal("Name", col3);
        Assert.Equal("▼", arr3); // SetSort resets to Descending on column change

        var order3 = NamesFromSource(s.Driver.PageSource);
        Assert.True(order3.Count >= 5, $"Expected >=5 rows, got {order3.Count}");
        var zebra3 = IndexOf(order3, "zebra-prod");
        var alpha3 = IndexOf(order3, "ALPHA-DEV");
        Assert.True(zebra3 >= 0 && alpha3 >= 0, "anchor identities must render");
        Assert.True(zebra3 < alpha3, "Descending Name sort: zebra precedes ALPHA");

        ById(s, "SortByName").Click(); // flip to Ascending
        await Task.Delay(SortSettleMs);
        var (col4, arr4) = ActiveSortArrow(s);
        Assert.Equal("Name", col4);
        Assert.Equal("▲", arr4);

        // Verify ascending case-insensitive ordering in a single PageSource fetch.
        var order4 = NamesFromSource(s.Driver.PageSource);
        var alpha4 = IndexOf(order4, "ALPHA-DEV");
        var bravo4 = IndexOf(order4, "Bravo-Staging");
        var charlie4 = IndexOf(order4, "CharlieEdge");
        var oscar4 = IndexOf(order4, "oscar-prod");
        var zebra4 = IndexOf(order4, "zebra-prod");
        Assert.True(alpha4 < bravo4 && bravo4 < charlie4 && charlie4 < oscar4 && oscar4 < zebra4,
            $"Ascending Name case-insensitive: expected ALPHA<Bravo<Charlie<oscar<zebra, got {alpha4}/{bravo4}/{charlie4}/{oscar4}/{zebra4}");
        SaveStep(s, name, "03-after-name-x2-ascending");

        // --- SERVICES x2 -----------------------------------------------------
        ById(s, "SortByServices").Click();
        await Task.Delay(SortSettleMs);
        var (col5, arr5) = ActiveSortArrow(s);
        Assert.Equal("Services", col5);
        Assert.Equal("▼", arr5);

        ById(s, "SortByServices").Click();
        await Task.Delay(SortSettleMs);
        var (col6, arr6) = ActiveSortArrow(s);
        Assert.Equal("Services", col6);
        Assert.Equal("▲", arr6);
        SaveStep(s, name, "04-after-services-x2-ascending");

        // Sanity: full list still renders after all 6 clicks. Cheap row count
        // check (no extra PageSource fetch -- we just took one for SaveStep).
        var finalOrder = NamesFromSource(s.Driver.PageSource);
        Assert.True(finalOrder.Count >= 5,
            $"After full walkthrough, expected >=5 rows, got {finalOrder.Count}");
    }
}
