using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// UI twin of ziti-tunnel-sdk-c tests/integration/set_log_level_test.go. The rejects subtests have no twin: the menu
/// only sends the levels it lists.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class SetLogLevelTests
{
    private static readonly string[] LevelIds = { "LogError", "LogWarn", "LogInfo", "LogDebug", "LogVerbose", "LogTrace" };

    private readonly IntegrationFixture _fixture;

    public SetLogLevelTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static By LevelChecked(string levelId) =>
        By.XPath($"//*[@AutomationId='{levelId}']//*[@AutomationId='SelectedCheck']");

    private static string CheckedLevelId(AppiumSession s)
    {
        List<string> checkedIds = LevelIds.Where(id => s.Driver.FindElements(LevelChecked(id)).Count > 0).ToList();
        Assert.True(checkedIds.Count == 1, $"expected one checked log level, found: {string.Join(", ", checkedIds)}");
        return checkedIds[0];
    }

    /// <summary>The LogLevel in ZET's config.json, or null while a read lands mid-write, as ZET's ReadTunnelConfig retries.</summary>
    private static string? SavedLogLevel(string path)
    {
        try
        {
            return (string?)JObject.Parse(File.ReadAllText(path))["LogLevel"];
        }
        catch (Exception e) when (e is IOException || e is JsonReaderException)
        {
            return null;
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Succeeds()
    {
        Trace.Begin();
        string name = nameof(Succeeds);

        await using AppiumSession s = await LaunchAsync(_fixture, name);
        OpenMainMenu(s);
        ClickUntil(s, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Set Logging Level']"));
        await Trace.Settle(350);
        SaveStep(s, name, "01-advanced-settings");
        await VerifyScreen(Capture(s), "advanced-settings");

        ClickUntil(s, By.XPath("//*[@Name='Set Logging Level']"), By.XPath("//*[@AutomationId='LogTrace']"));
        await Trace.Settle(350);
        SaveStep(s, name, "02-log-level-menu");
        await VerifyScreen(Capture(s), "log-level-menu");
        string startLevelId = CheckedLevelId(s);
        Assert.NotEqual("LogTrace", startLevelId);

        WaitForId(s, "LogTrace").Click();
        WaitFor(s, LevelChecked("LogTrace"));
        await Trace.Settle(350);
        SaveStep(s, name, "03-trace-set");
        await VerifyScreen(Capture(s), "trace-set");
        // The UI discards ZET's reply to SetLogLevel, so a checked Trace alone proves nothing about ZET.
        JObject reply = ZetReplyTo(s.Relay!, "\"Level\":\"trace\"");
        Assert.True((bool?)reply["Success"] == true, $"ZET rejected SetLogLevel trace: {reply}");
        // ZET replies before it saves config.json.
        string tunnelConfig = Path.Combine(_fixture.Zet.IdentityDir, "config.json");
        WaitUntil(s, "ZET saves the trace level", TimeSpan.FromSeconds(2),
            () => SavedLogLevel(tunnelConfig) == "trace");
        // The monitor gets its own {Op:"SetLogLevel", Action:"<level>"}, separate from ZET's.
        WaitUntil(s, "the trace level reaches the monitor", TimeSpan.FromSeconds(3),
            () => s.Mock.ReceivedMonitorRequests.Any(r => (string?)r["Op"] == "SetLogLevel" && (string?)r["Action"] == "trace"));

        WaitForId(s, startLevelId).Click();
        WaitFor(s, LevelChecked(startLevelId));
        SaveStep(s, name, "04-level-restored");
        string startLevel = startLevelId.Substring("Log".Length).ToLowerInvariant();
        JObject restoreReply = ZetReplyTo(s.Relay!, $"\"Level\":\"{startLevel}\"");
        Assert.True((bool?)restoreReply["Success"] == true, $"ZET rejected SetLogLevel {startLevel}: {restoreReply}");
    }
}
