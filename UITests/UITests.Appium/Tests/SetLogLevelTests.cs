using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class SetLogLevelTests
{
    private static readonly string[] LevelIds = { "LogError", "LogWarn", "LogInfo", "LogDebug", "LogVerbose", "LogTrace" };

    // The level items holding a SelectedCheck, which only the checked level's does. One lookup, since each costs ~800ms.
    private static readonly By CheckedLevels = By.XPath(
        $"//*[({string.Join(" or ", LevelIds.Select(id => $"@AutomationId='{id}'"))}) and .//*[@AutomationId='SelectedCheck']]");

    private readonly IntegrationFixture _fixture;

    public SetLogLevelTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static By LevelChecked(string levelId) =>
        By.XPath($"//*[@AutomationId='{levelId}']//*[@AutomationId='SelectedCheck']");

    private static string CheckedLevelId(AppiumSession s)
    {
        List<string> checkedIds = s.Driver.FindElements(CheckedLevels)
            .Select((AppiumElement e) => e.GetAttribute("AutomationId")).ToList();
        Assert.True(checkedIds.Count == 1, $"expected one checked log level, found: {string.Join(", ", checkedIds)}");
        return checkedIds[0];
    }

    [Fact(Timeout = 60000)]
    public async Task Succeeds()
    {
        string name = nameof(Succeeds);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        OpenMainMenu(s);
        ClickUntil(s, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Set Logging Level']"));
        await VerifyStep(Capture(s), name, "01-advanced-settings");

        ClickUntil(s, By.XPath("//*[@Name='Set Logging Level']"), ById("LogTrace"));
        await VerifyStep(Capture(s), name, "02-log-level-menu");
        string startLevelId = CheckedLevelId(s);
        Assert.NotEqual("LogTrace", startLevelId);

        WaitForId(s, "LogTrace").Click();
        WaitFor(s, LevelChecked("LogTrace"));
        await VerifyStep(Capture(s), name, "03-trace-set");
        // The UI discards ZET's reply to SetLogLevel, so a checked Trace alone proves nothing about ZET.
        JObject reply = ZetReplyTo(s.Relay!, "\"Level\":\"trace\"");
        Assert.True((bool?)reply["Success"] == true, $"ZET rejected SetLogLevel trace: {reply}");
        // ZET replies before it saves config.json.
        WaitUntil(s, "ZET saves the trace level", TimeSpan.FromSeconds(2),
            () => (string?)SavedZetConfig(_fixture)?["LogLevel"] == "trace");
        // The monitor gets its own {Op:"SetLogLevel", Action:"<level>"}, separate from ZET's.
        WaitUntil(s, "the trace level reaches the monitor", TimeSpan.FromSeconds(3),
            () => s.Mock.ReceivedMonitorRequests.Any(r => (string?)r["Op"] == "SetLogLevel" && (string?)r["Action"] == "trace"));

        // Later tests in the class share this ZET, so it goes back to the level it started at.
        WaitForId(s, startLevelId).Click();
        WaitFor(s, LevelChecked(startLevelId));
        string startLevel = startLevelId.Substring("Log".Length).ToLowerInvariant();
        JObject restoreReply = ZetReplyTo(s.Relay!, $"\"Level\":\"{startLevel}\"");
        Assert.True((bool?)restoreReply["Success"] == true, $"ZET rejected SetLogLevel {startLevel}: {restoreReply}");
    }
}
