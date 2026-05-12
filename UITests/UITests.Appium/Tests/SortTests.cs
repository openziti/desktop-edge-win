using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tests for the landing-screen identity sort headers (Status / Name / Services).
/// All tests use the 15-identity SortableMixed fixture with mixed capitalisation
/// and varied states. PageSource regex extraction is used instead of per-row
/// FindElement queries -- the latter is ~10s per query against a 15-row tree.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Sort")]
public class SortTests
{
    /// <summary>
    /// Pull the rendered identity names off the landing list, in tree order, by
    /// regex-matching the IdName Text elements in PageSource. Orders of magnitude
    /// faster than FindElements + .Text round-trips for 15 rows.
    /// </summary>
    private static List<string> RenderedIdentityOrder(AppiumSession s)
    {
        var src = s.Driver.PageSource;
        // The serialized UIA XML may emit attributes in either order. Try
        // AutomationId-first, then Name-first, and merge in document order.
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

    [Fact(Timeout = 120000)]
    public async Task Sort_ClickNameHeader_SortsAlphabetically()
    {
        // The UI's DEFAULT sort is by Status (active first, then insertion order),
        // not alphabetical. To get alphabetical order we have to activate the Name
        // sort first by clicking its header.
        var name = nameof(Sort_ClickNameHeader_SortsAlphabetically);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(800);
        SaveStep(s, name, "01-landing-default-sort-by-status");

        ById(s, "SortByName").Click();
        await Task.Delay(500);
        SaveStep(s, name, "02-after-name-sort-active");

        var order = RenderedIdentityOrder(s);
        Assert.NotEmpty(order);
        var alphaIdx = order.FindIndex(n => n.Equals("ALPHA-DEV", StringComparison.OrdinalIgnoreCase));
        var zebraIdx = order.FindIndex(n => n.Equals("zebra-prod", StringComparison.OrdinalIgnoreCase));
        Assert.True(alphaIdx >= 0 && zebraIdx >= 0);
        Assert.True(alphaIdx < zebraIdx,
            $"After clicking SortByName, expected ALPHA-DEV (#{alphaIdx}) before zebra-prod (#{zebraIdx}). Order: [{string.Join(", ", order)}]");
    }

    [Fact(Timeout = 120000)]
    public async Task Sort_ClickNameHeader_TogglesArrowBothWays()
    {
        var name = nameof(Sort_ClickNameHeader_TogglesArrowBothWays);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(800);
        SaveStep(s, name, "01-landing-default-sort-by-status");

        // Default sort is Status, so SortByNameArrow is not in the UIA tree yet.
        Assert.Equal("", TryGetTextById(s, "SortByNameArrow"));

        // Click 1: Name header becomes active sort -- arrow appears.
        ById(s, "SortByName").Click();
        await Task.Delay(400);
        SaveStep(s, name, "02-after-first-name-click");
        var arrow1 = TryGetTextById(s, "SortByNameArrow");
        Assert.True(arrow1 == "▲" || arrow1 == "▼",
            $"Expected SortByNameArrow to render ▲ or ▼ after first click, got '{arrow1}'");

        // Click 2: same header -> arrow flips direction.
        ById(s, "SortByName").Click();
        await Task.Delay(400);
        SaveStep(s, name, "03-after-second-name-click");
        var arrow2 = TryGetTextById(s, "SortByNameArrow");
        Assert.True(arrow2 == "▲" || arrow2 == "▼");
        Assert.NotEqual(arrow1, arrow2);

        // Click 3: click the arrow itself -> flips back to arrow1.
        ById(s, "SortByNameArrow").Click();
        await Task.Delay(400);
        SaveStep(s, name, "04-after-clicking-arrow-itself");
        var arrow3 = TryGetTextById(s, "SortByNameArrow");
        Assert.Equal(arrow1, arrow3);
    }

    [Fact(Timeout = 120000)]
    public async Task Sort_ClickStatusHeader_GroupsByStatus()
    {
        var name = nameof(Sort_ClickStatusHeader_GroupsByStatus);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(800);
        SaveStep(s, name, "01-landing-before-status-click");

        ById(s, "SortByStatus").Click();
        await Task.Delay(500);
        SaveStep(s, name, "02-after-status-header-click");

        // Use PageSource character offsets of ENABLED / DISABLED status Text nodes to
        // verify they cluster contiguously. After grouping there should be no
        // alternation between the two states -- i.e. one contiguous run each.
        var src = s.Driver.PageSource;
        var enabledIdxs = Regex.Matches(src, "Name=\"ENABLED\"").Select(m => m.Index).ToList();
        var disabledIdxs = Regex.Matches(src, "Name=\"DISABLED\"").Select(m => m.Index).ToList();
        Assert.NotEmpty(enabledIdxs);
        Assert.NotEmpty(disabledIdxs);

        // All ENABLED indices fall on one side of all DISABLED indices (no interleaving).
        var allEnabledBeforeAllDisabled = enabledIdxs.Max() < disabledIdxs.Min();
        var allDisabledBeforeAllEnabled = disabledIdxs.Max() < enabledIdxs.Min();
        Assert.True(allEnabledBeforeAllDisabled || allDisabledBeforeAllEnabled,
            "Expected ENABLED and DISABLED status labels to cluster (no interleaving) after sorting by status.");
    }

    [Fact(Timeout = 120000)]
    public async Task Sort_ClickServicesHeader_TogglesBothWays()
    {
        var name = nameof(Sort_ClickServicesHeader_TogglesBothWays);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(800);
        SaveStep(s, name, "01-landing-services-arrow-absent");

        // Each column only renders its arrow when it's the ACTIVE sort. Services
        // isn't the default, so before we click anything its arrow shouldn't exist.
        Assert.Equal("", TryGetTextById(s, "SortByServicesArrow"));

        // Step 1: click the Services label text -- arrow should appear.
        ById(s, "SortByServices").Click();
        await Task.Delay(400);
        SaveStep(s, name, "02-after-clicking-services-label");

        var arrowFirst = TryGetTextById(s, "SortByServicesArrow");
        Assert.True(arrowFirst == "▲" || arrowFirst == "▼",
            $"Expected SortByServicesArrow to render ▲ or ▼ after first click, got '{arrowFirst}'");

        // Step 2: click the arrow itself -- direction should flip.
        ById(s, "SortByServicesArrow").Click();
        await Task.Delay(400);
        SaveStep(s, name, "03-after-clicking-arrow-once");

        var arrowSecond = TryGetTextById(s, "SortByServicesArrow");
        Assert.True(arrowSecond == "▲" || arrowSecond == "▼",
            $"Expected SortByServicesArrow to render ▲ or ▼ after arrow click, got '{arrowSecond}'");
        Assert.NotEqual(arrowFirst, arrowSecond);

        // Step 3: click the arrow again -- should flip back to the first state.
        ById(s, "SortByServicesArrow").Click();
        await Task.Delay(400);
        SaveStep(s, name, "04-after-clicking-arrow-twice");

        var arrowThird = TryGetTextById(s, "SortByServicesArrow");
        Assert.Equal(arrowFirst, arrowThird);

        // The list must still render all 15 identities through every toggle.
        var order = RenderedIdentityOrder(s);
        Assert.True(order.Count >= 15, $"Expected >=15 rendered identities, got {order.Count}");
    }

    [Fact(Timeout = 120000)]
    public async Task Sort_CaseInsensitivity_AlphaBeforeBravoBeforeCharlie()
    {
        var name = nameof(Sort_CaseInsensitivity_AlphaBeforeBravoBeforeCharlie);
        await using var s = await AppiumSession.LaunchAsync(DefaultExePath(), FixtureBuilder.SortableMixed());
        WaitForId(s, "ConnectLabel");
        await Task.Delay(800);
        SaveStep(s, name, "01-landing-default-sort-by-status");

        // Activate alphabetical sort before checking case-insensitivity.
        ById(s, "SortByName").Click();
        await Task.Delay(500);
        SaveStep(s, name, "02-after-name-sort-active");

        var order = RenderedIdentityOrder(s);
        Assert.NotEmpty(order);

        int IdxOf(string needle) => order.FindIndex(n => n.Equals(needle, StringComparison.OrdinalIgnoreCase));

        var alpha = IdxOf("ALPHA-DEV");
        var bravo = IdxOf("Bravo-Staging");
        var charlie = IdxOf("CharlieEdge");
        var zebra = IdxOf("zebra-prod");
        var oscar = IdxOf("oscar-prod");

        Assert.True(alpha >= 0, $"ALPHA-DEV missing in [{string.Join(", ", order)}]");
        Assert.True(bravo >= 0, $"Bravo-Staging missing in [{string.Join(", ", order)}]");
        Assert.True(charlie >= 0, $"CharlieEdge missing in [{string.Join(", ", order)}]");
        Assert.True(zebra >= 0, $"zebra-prod missing in [{string.Join(", ", order)}]");
        Assert.True(oscar >= 0, $"oscar-prod missing in [{string.Join(", ", order)}]");

        Assert.True(alpha < bravo, $"alpha (#{alpha}) should precede bravo (#{bravo})");
        Assert.True(bravo < charlie, $"bravo (#{bravo}) should precede charlie (#{charlie})");
        Assert.True(charlie < oscar, $"charlie (#{charlie}) should precede oscar (#{oscar})");
        Assert.True(oscar < zebra, $"oscar (#{oscar}) should precede zebra (#{zebra})");
    }
}
