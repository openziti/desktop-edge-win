using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Sort")]
public class SortTests
{
    public SortTests(ITestOutputHelper output)
    {
        Step.Use(output);
    }

    private static readonly string[] Disabled = { "Bravo-Staging", "CharlieEdge" };
    private static readonly string[] Enabled = { "zebra-prod", "ALPHA-DEV", "oscar-prod" };

    private static void AssertLeads(List<string> order, string[] group) =>
        Assert.Equal(group.Order(), order.Take(group.Length).Order());

    private static string Flipped(string arrow) => arrow == "▲" ? "▼" : "▲";

    // SetSort puts a newly chosen column in descending order and flips the active one.
    private static string ClickTwice(AppiumSession s, IWebElement header, string column)
    {
        (string activeColumn, string activeArrow) = ActiveSortArrow(s);
        string first = activeColumn == column ? Flipped(activeArrow) : "▼";
        ClickSortHeader(s, header, (column, first));
        ClickSortHeader(s, header, (column, Flipped(first)));
        return Flipped(first);
    }

    // About 30s of body, plus up to 30s of cold first app launch when the shuffled class order runs this first.
    [Fact(Timeout = 120000)]
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
        // Services sorts by auth state first, so ascending leads with CharlieEdge, the only one needing ext auth.
        Assert.Equal("CharlieEdge", ListedNames(s)[0]);

        Assert.Equal("▲", ClickTwice(s, nameHeader, "Name"));
        SaveStep(s, name, "03-after-name-x2");
        // Name ascending ignores case.
        Assert.Equal(new[] { "ALPHA-DEV", "Bravo-Staging", "CharlieEdge", "oscar-prod", "zebra-prod" }, ListedNames(s));
    }
}
