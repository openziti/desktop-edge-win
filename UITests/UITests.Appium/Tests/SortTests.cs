using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Clicks the Status, Services and Name headers twice each on the SortableMixed fixture, checking the active arrow
/// after every click and the order after every pair.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Sort")]
public class SortTests
{
    // Enabled state in SortableMixed.
    private static readonly string[] Disabled = { "Bravo-Staging", "CharlieEdge" };
    private static readonly string[] Enabled = { "zebra-prod", "ALPHA-DEV", "oscar-prod" };

    /// <summary>The group fills the top of the list, in any order.</summary>
    private static void AssertLeads(List<string> order, string[] group) =>
        Assert.Equal(group.Order(), order.Take(group.Length).Order());

    private static string Flipped(string arrow) => arrow == "▲" ? "▼" : "▲";

    /// <summary>
    /// Two clicks on a header. SetSort puts a newly chosen column in descending order and flips the active one, so
    /// the first click's arrow depends on where the sort was.
    /// </summary>
    private static string ClickTwice(AppiumSession s, IWebElement header, string column)
    {
        (string activeColumn, string activeArrow) = ActiveSortArrow(s);
        string first = activeColumn == column ? Flipped(activeArrow) : "▼";
        ClickSortHeader(s, header, (column, first));
        ClickSortHeader(s, header, (column, Flipped(first)));
        return Flipped(first);
    }

    [Fact(Timeout = 60000)]
    public async Task SortHeadersReorderIdentities()
    {
        string name = nameof(SortHeadersReorderIdentities);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed(),
            UiLogPath(name));
        IWebElement statusHeader = WaitForId(s, "SortByStatus");
        IWebElement servicesHeader = WaitForId(s, "SortByServices");
        IWebElement nameHeader = WaitForId(s, "SortByName");

        // The sort persists in the user's user.config between runs, so the Status direction depends on where it starts.
        string statusArrow = ClickTwice(s, statusHeader, "Status");
        SaveStep(s, name, "01-after-status-x2");
        // Status sorts by enabled state, and ascending puts disabled identities first.
        AssertLeads(ListedNames(s), statusArrow == "▲" ? Disabled : Enabled);

        Assert.Equal("▲", ClickTwice(s, servicesHeader, "Services"));
        SaveStep(s, name, "02-after-services-x2");
        // Services sorts by auth state before service count, and no fixture identity has services. Ascending puts
        // CharlieEdge, the only one needing external auth, first.
        Assert.Equal("CharlieEdge", ListedNames(s)[0]);

        Assert.Equal("▲", ClickTwice(s, nameHeader, "Name"));
        SaveStep(s, name, "03-after-name-x2");
        // Name ascending ignores case.
        Assert.Equal(new[] { "ALPHA-DEV", "Bravo-Staging", "CharlieEdge", "oscar-prod", "zebra-prod" }, ListedNames(s));
    }
}
