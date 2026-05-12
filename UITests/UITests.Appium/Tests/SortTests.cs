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
/// Why one big test instead of five small ones:
///   * Sort state is persisted via Properties.Settings.Save(), so individual
///     tests leaked state into each other and depended on undefined run order.
///   * Each fresh AppiumSession is ~3-5s of overhead; folding the cases into
///     one launch is meaningfully faster than five launches.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Sort")]
public class SortTests
{
    /// <summary>
    /// Pull the rendered identity names off the landing list, in tree order,
    /// via PageSource regex. Orders of magnitude faster than per-row queries
    /// against a 15-row tree.
    /// </summary>
    private static List<string> RenderedIdentityOrder(AppiumSession s)
    {
        var src = s.Driver.PageSource;
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
    /// Returns the active sort arrow's text. The arrow TextBlock for an inactive
    /// column is Collapsed and not in the UIA tree, so only one of the three
    /// queries returns a value.
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

    /// <summary>
    /// Find the position of a name in the rendered order, case-insensitively.
    /// Returns -1 if not found.
    /// </summary>
    private static int IndexOf(List<string> order, string needle) =>
        order.FindIndex(n => n.Equals(needle, StringComparison.OrdinalIgnoreCase));

    [Fact(Timeout = 240000)]
    public async Task Sort_FullWalkthrough_StatusNameServicesEachClickedTwice()
    {
        var name = nameof(Sort_FullWalkthrough_StatusNameServicesEachClickedTwice);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(300); // let all 15 rows render
        SaveStep(s, name, "01-landing-persisted-sort");

        // --- STATUS, click 1 ---------------------------------------------------
        ById(s, "SortByStatus").Click();
        await Task.Delay(150);
        SaveStep(s, name, "02-after-status-click-1");
        var (col1, arr1) = ActiveSortArrow(s);
        Assert.Equal("Status", col1);
        Assert.True(arr1 == "▲" || arr1 == "▼");

        // ENABLED + DISABLED clusters (no interleaving) after sorting by status.
        var src1 = s.Driver.PageSource;
        var enabled1 = Regex.Matches(src1, "Name=\"ENABLED\"").Select(m => m.Index).ToList();
        var disabled1 = Regex.Matches(src1, "Name=\"DISABLED\"").Select(m => m.Index).ToList();
        Assert.NotEmpty(enabled1);
        Assert.NotEmpty(disabled1);
        var grouped = enabled1.Max() < disabled1.Min() || disabled1.Max() < enabled1.Min();
        Assert.True(grouped, "ENABLED and DISABLED labels should cluster contiguously after Status sort.");

        // --- STATUS, click 2 (toggles direction, stays on Status) -------------
        ById(s, "SortByStatus").Click();
        await Task.Delay(150);
        SaveStep(s, name, "03-after-status-click-2");
        var (col2, arr2) = ActiveSortArrow(s);
        Assert.Equal("Status", col2);
        Assert.True(arr2 == "▲" || arr2 == "▼");
        Assert.NotEqual(arr1, arr2);

        // Still clustered after the direction flip.
        var src2 = s.Driver.PageSource;
        var enabled2 = Regex.Matches(src2, "Name=\"ENABLED\"").Select(m => m.Index).ToList();
        var disabled2 = Regex.Matches(src2, "Name=\"DISABLED\"").Select(m => m.Index).ToList();
        Assert.True(enabled2.Max() < disabled2.Min() || disabled2.Max() < enabled2.Min(),
            "Cluster invariant must hold after the Status direction flip.");

        // --- NAME, click 1 (switches column, defaults to Descending) ----------
        ById(s, "SortByName").Click();
        await Task.Delay(150);
        SaveStep(s, name, "04-after-name-click-1");
        var (col3, arr3) = ActiveSortArrow(s);
        Assert.Equal("Name", col3);
        Assert.Equal("▼", arr3); // SetSort() switches direction back to Descending when changing column

        // Descending alphabetical: zebra-prod precedes ALPHA-DEV.
        var order3 = RenderedIdentityOrder(s);
        Assert.True(order3.Count >= 15, $"Expected >=15 rows, got {order3.Count}: [{string.Join(", ", order3)}]");
        var zebra3 = IndexOf(order3, "zebra-prod");
        var alpha3 = IndexOf(order3, "ALPHA-DEV");
        Assert.True(zebra3 >= 0 && alpha3 >= 0,
            $"zebra-prod ({zebra3}) and ALPHA-DEV ({alpha3}) must both render. Order: [{string.Join(", ", order3)}]");
        Assert.True(zebra3 < alpha3,
            $"Descending Name sort: zebra-prod (#{zebra3}) should precede ALPHA-DEV (#{alpha3}).");

        // --- NAME, click 2 (flips to Ascending) -------------------------------
        ById(s, "SortByName").Click();
        await Task.Delay(150);
        SaveStep(s, name, "05-after-name-click-2");
        var (col4, arr4) = ActiveSortArrow(s);
        Assert.Equal("Name", col4);
        Assert.Equal("▲", arr4);

        // Ascending alphabetical: ALPHA-DEV first, zebra-prod last.
        // Also verify case-insensitivity: ALPHA-DEV < Bravo-Staging < CharlieEdge.
        var order4 = RenderedIdentityOrder(s);
        var alpha4 = IndexOf(order4, "ALPHA-DEV");
        var bravo4 = IndexOf(order4, "Bravo-Staging");
        var charlie4 = IndexOf(order4, "CharlieEdge");
        var oscar4 = IndexOf(order4, "oscar-prod");
        var zebra4 = IndexOf(order4, "zebra-prod");
        Assert.True(alpha4 >= 0 && bravo4 >= 0 && charlie4 >= 0 && oscar4 >= 0 && zebra4 >= 0,
            $"All five anchor identities must render. Order: [{string.Join(", ", order4)}]");
        Assert.True(alpha4 < bravo4 && bravo4 < charlie4 && charlie4 < oscar4 && oscar4 < zebra4,
            $"Ascending case-insensitive Name sort expected ALPHA<Bravo<Charlie<oscar<zebra; got {alpha4}/{bravo4}/{charlie4}/{oscar4}/{zebra4} in [{string.Join(", ", order4)}]");

        // --- SERVICES, click 1 (switches column, defaults to Descending) ------
        ById(s, "SortByServices").Click();
        await Task.Delay(150);
        SaveStep(s, name, "06-after-services-click-1");
        var (col5, arr5) = ActiveSortArrow(s);
        Assert.Equal("Services", col5);
        Assert.Equal("▼", arr5);

        // --- SERVICES, click 2 (flips direction, stays on Services) -----------
        ById(s, "SortByServices").Click();
        await Task.Delay(150);
        SaveStep(s, name, "07-after-services-click-2");
        var (col6, arr6) = ActiveSortArrow(s);
        Assert.Equal("Services", col6);
        Assert.Equal("▲", arr6);

        // The list must still render all 15 identities through every toggle.
        var finalOrder = RenderedIdentityOrder(s);
        Assert.True(finalOrder.Count >= 15,
            $"After full Status/Name/Services walkthrough, expected >=15 rows, got {finalOrder.Count}");

        // --- Return to a known sort so subsequent test runs are deterministic.
        // SetSort persists to Properties.Settings; leave Status active.
        ById(s, "SortByStatus").Click();
        await Task.Delay(200);
        SaveStep(s, name, "08-final-reset-to-status");
    }
}
