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

    // About 30s of body, plus up to 30s of cold first app launch when the shuffled class order runs this first.
    [Fact(Timeout = 120000)]
    public async Task SortHeadersReorderIdentities()
    {
        string name = nameof(SortHeadersReorderIdentities);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed(),
            UiLogPath(name));
        SortAscending(s, "Name");

        SortAscending(s, "Status");
        SaveStep(s, name, "01-after-status-x2");
        // Status sorts by enabled state, and ascending puts the two disabled identities first.
        Assert.Equal(new[] { "Bravo-Staging", "CharlieEdge" }, ListedNames(s).Take(2).Order());

        SortAscending(s, "Services");
        SaveStep(s, name, "02-after-services-x2");
        // Services sorts by auth state first, so ascending leads with CharlieEdge, the only one needing ext auth.
        Assert.Equal("CharlieEdge", ListedNames(s)[0]);

        ClickSortHeader(s, WaitForId(s, "SortByName"), ("Name", "▼"));
        Assert.Equal(new[] { "zebra-prod", "oscar-prod", "CharlieEdge", "Bravo-Staging", "ALPHA-DEV" }, ListedNames(s));
        ClickSortHeader(s, WaitForId(s, "SortByName"), ("Name", "▲"));
        SaveStep(s, name, "03-after-name-x2");
        Assert.Equal(new[] { "ALPHA-DEV", "Bravo-Staging", "CharlieEdge", "oscar-prod", "zebra-prod" }, ListedNames(s));
    }
}
