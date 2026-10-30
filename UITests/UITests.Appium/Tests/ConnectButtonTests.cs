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

    private static void ClickUntilMonitorGets(AppiumSession session, By target, string operation) =>
        ClickUntil(session, target, $"the monitor gets {operation}", () => session.Mock.ReceivedMonitorRequests.Any(request => (string?)request["Op"] == operation));

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task DisconnectStopsAndConnectStartsTheService()
    {
        string name = nameof(DisconnectStopsAndConnectStartsTheService);
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitFor(session, ConnectLabel("Tap to Disconnect"));
        WaitFor(session, By.XPath(IdentityRowXPath("enabled-id")));

        ClickUntilMonitorGets(session, Stop, "Stop");
        WaitFor(session, ConnectLabel("Tap to Connect"));
        WaitForGone(session, LoadingTitle);
        Assert.Single(session.Mock.ReceivedMonitorRequests, request => (string?)request["Op"] == "Stop" && (string?)request["Action"] == "Normal");
        Assert.Empty(session.Driver.FindElements(By.XPath(IdentityRowXPath("enabled-id"))));
        await VerifyStep(session, Capture(session), name, "01-service-stopped");

        ClickUntilMonitorGets(session, ConnectImage, "Start");
        // The UI's data client retries its pipe every 2.5s, so the reconnect lands after the Start reply.
        WaitFor(session, ConnectLabel("Tap to Disconnect"));
        WaitFor(session, By.XPath(IdentityRowXPath("enabled-id")));
        WaitForGone(session, LoadingTitle);
        JObject start = Assert.Single(session.Mock.ReceivedMonitorRequests, request => (string?)request["Op"] == "Start");
        Assert.Equal("Normal", (string?)start["Action"]);
        await VerifyStep(session, Capture(session), name, "02-service-started");
    }

    [Fact(Timeout = 60000)]
    [Trait("Category", "MainScreen")]
    [Trait("Category", "Screenshots")]
    public async Task FailedStopAndStartShowTheMonitorError()
    {
        string name = nameof(FailedStopAndStartShowTheMonitorError);
        // What ServiceController.WaitForStatus throws when the ziti service misses the monitor's wait.
        const string failure = "Time out has expired and the operation has not been completed.";
        await using AppiumSession session = await AppiumSession.LaunchAsync(DefaultExePath(), Fixture("landing-status.json"),
            UiLogPath(name));
        WaitFor(session, ConnectLabel("Tap to Disconnect"));
        session.Mock.ServiceActionFailure = failure;

        ClickUntilMonitorGets(session, Stop, "Stop");
        WaitFor(session, ErrorTitle("Error Disabling Service"));
        WaitForGone(session, LoadingTitle);
        Assert.Equal(failure + ":", TextById(session, "ErrorDetails"));
        await VerifyStep(session, Capture(session), name, "01-stop-failed");

        ClickUntilGone(session, CloseErrorButton);
        ClickUntilMonitorGets(session, ConnectImage, "Start");
        WaitFor(session, ErrorTitle("Error Starting Service"));
        WaitForGone(session, LoadingTitle);
        Assert.Equal(failure + ":", TextById(session, "ErrorDetails"));
        await VerifyStep(session, Capture(session), name, "02-start-failed");
    }
}
