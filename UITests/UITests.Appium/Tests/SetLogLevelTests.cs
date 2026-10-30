using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class SetLogLevelTests
{
    private readonly IntegrationFixture _fixture;

    public SetLogLevelTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    private static By LevelChecked(string levelId) =>
        By.XPath($"//*[@AutomationId='{levelId}']//*[@AutomationId='SelectedCheck']");

    [Fact(Timeout = 60000)]
    public async Task Succeeds()
    {
        string name = nameof(Succeeds);

        await using AppiumSession session = await LaunchAsync(_fixture, name);
        OpenMainMenu(session);
        ClickUntil(session, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Set Logging Level']"));
        await VerifyStep(session, Capture(session), name, "01-advanced-settings");

        ClickUntil(session, By.XPath("//*[@Name='Set Logging Level']"), ById("LogTrace"));
        await VerifyStep(session, Capture(session), name, "02-log-level-menu");
        string[] levelIds = { "LogError", "LogWarn", "LogInfo", "LogDebug", "LogVerbose", "LogTrace" };
        // Only the checked level holds a SelectedCheck. One lookup, since each costs ~800ms.
        By checkedLevels = By.XPath(
            $"//*[({string.Join(" or ", levelIds.Select(id => $"@AutomationId='{id}'"))}) and .//*[@AutomationId='SelectedCheck']]");
        List<string> checkedIds = session.Driver.FindElements(checkedLevels)
            .Select((AppiumElement level) => level.GetAttribute("AutomationId")).ToList();
        Assert.True(checkedIds.Count == 1, $"expected one checked log level, found: {string.Join(", ", checkedIds)}");
        string startLevelId = checkedIds[0];
        Assert.NotEqual("LogTrace", startLevelId);

        WaitForId(session, "LogTrace").Click();
        WaitFor(session, LevelChecked("LogTrace"));
        await VerifyStep(session, Capture(session), name, "03-trace-set");
        // The UI discards ZET's reply to SetLogLevel, so a checked Trace alone proves nothing about ZET.
        JObject reply = WaitForZetReplyTo(session, "\"Level\":\"trace\"");
        Assert.True((bool?)reply["Success"] == true, $"ZET rejected SetLogLevel trace: {reply}");
        // ZET replies before it saves config.json.
        WaitUntil(session, "ZET saves the trace level", TimeSpan.FromSeconds(2),
            () => (string?)SavedZetConfig(_fixture)?["LogLevel"] == "trace");
        WaitUntil(session, "the trace level reaches the monitor", TimeSpan.FromSeconds(3),
            () => session.Mock.ReceivedMonitorRequests.Any(request => (string?)request["Op"] == "SetLogLevel" && (string?)request["Action"] == "trace"));

        // Later tests in the class share this ZET, so it goes back to the level it started at.
        WaitForId(session, startLevelId).Click();
        WaitFor(session, LevelChecked(startLevelId));
        string startLevel = startLevelId.Substring("Log".Length).ToLowerInvariant();
        JObject restoreReply = WaitForZetReplyTo(session, $"\"Level\":\"{startLevel}\"");
        Assert.True((bool?)restoreReply["Success"] == true, $"ZET rejected SetLogLevel {startLevel}: {restoreReply}");
    }
}
