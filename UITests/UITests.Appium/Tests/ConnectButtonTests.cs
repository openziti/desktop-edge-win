using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

public class ConnectButtonTests
{
    public ConnectButtonTests(ITestOutputHelper output)
    {
        Step.Use(output);
    }

    private static By ConnectLabel(string text) => By.XPath($"//*[@AutomationId='ConnectLabel' and @Name='{text}']");

    // Collapsed with the loading overlay, so it is in the tree only while the overlay shows.
    private static readonly By LoadingTitle = ById("LoadingTitle");
    // The STOP label has a UIA peer and its click bubbles up to the DisconnectButton canvas, which has none.
    private static readonly By Stop = By.XPath("//Text[@Name='STOP']");
    private static readonly By ConnectImage = ById("ConnectImage");

    private static void ClickUntilMonitorGets(AppiumSession s, By target, string op) =>
        ClickUntil(s, target, $"the monitor gets {op}", () => s.Mock.ReceivedMonitorRequests.Any(r => (string?)r["Op"] == op));

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task DisconnectStopsAndConnectStartsTheService()
    {
        string name = nameof(DisconnectStopsAndConnectStartsTheService);
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitFor(s, ConnectLabel("Tap to Disconnect"));
        WaitFor(s, By.XPath(IdentityRowXPath("enabled-id")));

        ClickUntilMonitorGets(s, Stop, "Stop");
        WaitFor(s, ConnectLabel("Tap to Connect"));
        WaitForGone(s, LoadingTitle);
        Assert.Single(s.Mock.ReceivedMonitorRequests, r => (string?)r["Op"] == "Stop" && (string?)r["Action"] == "Normal");
        Assert.Empty(s.Driver.FindElements(By.XPath(IdentityRowXPath("enabled-id"))));
        await VerifyStep(s, Capture(s), name, "01-service-stopped");

        ClickUntilMonitorGets(s, ConnectImage, "Start");
        // The UI's data client retries its pipe every 2.5s, so the reconnect lands after the Start reply.
        WaitFor(s, ConnectLabel("Tap to Disconnect"));
        WaitFor(s, By.XPath(IdentityRowXPath("enabled-id")));
        WaitForGone(s, LoadingTitle);
        JObject start = Assert.Single(s.Mock.ReceivedMonitorRequests, r => (string?)r["Op"] == "Start");
        Assert.Equal("Normal", (string?)start["Action"]);
        await VerifyStep(s, Capture(s), name, "02-service-started");
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task FailedStopAndStartShowTheMonitorError()
    {
        string name = nameof(FailedStopAndStartShowTheMonitorError);
        // What ServiceController.WaitForStatus throws when the ziti service misses the monitor's wait.
        const string failure = "Time out has expired and the operation has not been completed.";
        await using AppiumSession s = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitFor(s, ConnectLabel("Tap to Disconnect"));
        s.Mock.ServiceActionFailure = failure;

        ClickUntilMonitorGets(s, Stop, "Stop");
        WaitFor(s, ErrorTitle("Error Disabling Service"));
        WaitForGone(s, LoadingTitle);
        Assert.Equal(failure + ":", TextById(s, "ErrorDetails"));
        await VerifyStep(s, Capture(s), name, "01-stop-failed");

        ClickUntilGone(s, CloseErrorButton);
        ClickUntilMonitorGets(s, ConnectImage, "Start");
        WaitFor(s, ErrorTitle("Error Starting Service"));
        WaitForGone(s, LoadingTitle);
        Assert.Equal(failure + ":", TextById(s, "ErrorDetails"));
        await VerifyStep(s, Capture(s), name, "02-start-failed");
    }
}
