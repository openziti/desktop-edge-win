using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// The blurb never shows ZET's reason, since DataClient turns any failed reply into one generic ServiceException.
[Trait("Category", "Integration")]
[Collection(IntegrationCollection.Name)]
public class UpdateTunIpTests
{
    private const string UpdateInterfaceConfigLine = "\"Command\":\"UpdateInterfaceConfig\"";
    private const string ValidIp = "100.64.0.1";
    private const int ValidPrefixLength = 16;
    // Not the fixture's /16, so a save has to change the mask too.
    private const int SavedPrefixLength = 17;
    private const string InvalidIpError = "Invalid IP address";
    private const string PrefixLengthError = "prefix length should be between 10 and 18";

    private readonly IntegrationFixture _fixture;

    public UpdateTunIpTests(IntegrationFixture fixture, ITestOutputHelper output)
    {
        Step.Use(output);
        _fixture = fixture;
    }

    [Fact(Timeout = 60000)]
    public async Task WithEmptyIpRejected() =>
        await AssertSaveRejected(nameof(WithEmptyIpRejected), "", ValidPrefixLength, InvalidIpError);

    [Fact(Timeout = 60000)]
    public async Task WithWhitespaceIpRejected() =>
        await AssertSaveRejected(nameof(WithWhitespaceIpRejected), "   ", ValidPrefixLength, InvalidIpError);

    [Fact(Timeout = 60000)]
    public async Task WithPrefixTooSmallRejected() =>
        await AssertSaveRejected(nameof(WithPrefixTooSmallRejected), ValidIp, 9, PrefixLengthError);

    [Fact(Timeout = 60000)]
    public async Task WithPrefixTooLargeRejected() =>
        await AssertSaveRejected(nameof(WithPrefixTooLargeRejected), ValidIp, 25, PrefixLengthError);

    [Fact(Timeout = 60000)]
    public async Task WithMalformedIpRejected() =>
        await AssertSaveRejected(nameof(WithMalformedIpRejected), "not-an-ip", ValidPrefixLength, InvalidIpError);

    [Fact(Timeout = 60000)]
    public async Task ValidValuesSaved()
    {
        string name = nameof(ValidValuesSaved);
        // Later tests share this ZET, so its starting tun IP goes back afterwards.
        JObject start = (JObject)(await _fixture.Zet.SendCommandAsync(new JObject { ["Command"] = "Status" }))["Data"]!;
        Assert.NotEqual(ValidIp, (string?)start["TunIpv4"]);
        Assert.NotEqual(SavedPrefixLength, (int?)start["TunIpv4Mask"]);
        try
        {
            await using AppiumSession session = await LaunchAsync(_fixture, name);
            JObject reply = await SaveValues(session, name, ValidIp, SavedPrefixLength);
            Assert.Equal(0, (int?)reply["Code"]);
            await VerifyStep(session, CaptureBlurbOnReply(session, UpdateInterfaceConfigLine), name, "02-saved-blurb");
            // ZET replies before it saves config.json.
            WaitUntil(session, "ZET saves the new tun IP", TimeSpan.FromSeconds(2), () =>
            {
                JObject? saved = SavedZetConfig(_fixture);
                return (string?)saved?["TunIpv4"] == ValidIp && (int?)saved?["TunIpv4Mask"] == SavedPrefixLength;
            });
        }
        finally
        {
            await _fixture.Zet.SendCommandAsync(new JObject
            {
                ["Command"] = "UpdateTunIpv4",
                ["Data"] = new JObject
                {
                    ["TunIPv4"] = start["TunIpv4"],
                    ["TunPrefixLength"] = start["TunIpv4Mask"],
                    ["AddDns"] = start["AddDns"],
                },
            });
        }
    }

    private async Task AssertSaveRejected(string name, string ip, int prefixLength, string zetError)
    {
        await using AppiumSession session = await LaunchAsync(_fixture, name);
        JObject reply = await SaveValues(session, name, ip, prefixLength);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Equal(zetError, (string?)reply["Error"]);
        await VerifyStep(session, CaptureBlurbOnReply(session, UpdateInterfaceConfigLine), name, "02-rejected-blurb");
    }

    private static async Task<JObject> SaveValues(AppiumSession session, string name, string ip, int prefixLength)
    {
        OpenMainMenu(session);
        ClickUntil(session, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Tunnel Config']"));
        ClickUntil(session, By.XPath("//*[@Name='Tunnel Config']"), By.XPath("//*[@Name='Edit Values']"));
        ClickUntil(session, By.XPath("//*[@Name='Edit Values']"), By.XPath("//*[@Name='Save']"));

        TypeUntil(session, ById("ConfigIpNew"), ip);
        // The dropdown shows 15 of its 32 rows around the current mask, so a far row stays offscreen until Home or End.
        string jump;
        if (prefixLength <= 15)
            jump = Keys.Home;
        else
            jump = Keys.End;
        ClickUntil(session, ById("ConfigMaskNew"), By.XPath("//ListItem[@IsOffscreen='False']"));
        // Scoped to the ComboBox, these XPaths walk its rows instead of the whole UIA tree.
        IWebElement combo = WaitForId(session, "ConfigMaskNew");
        uint mask = uint.MaxValue << (32 - prefixLength);
        string maskItemName =
            $"/{prefixLength} - {mask >> 24}.{(mask >> 16) & 255}.{(mask >> 8) & 255}.{mask & 255}";
        By item = By.XPath($".//ListItem[@Name='{maskItemName}']");
        By onscreenItem = By.XPath(".//ListItem[@IsOffscreen='False']");
        IWebElement? row = null;
        // Pressed until the row shows, since WinAppDriver can drop a keystroke.
        WaitUntil(session, $"/{prefixLength} scrolls into view", TimeSpan.FromSeconds(20), () =>
        {
            row = combo.FindElements(item).FirstOrDefault(element => element.Displayed);
            if (row != null)
            {
                return true;
            }
            combo.FindElements(onscreenItem).First().SendKeys(jump);
            return false;
        });
        ClickAt(session, row!);
        WaitUntil(session, "the mask dropdown closes", AppResponseTimeout,
            () => combo.FindElements(onscreenItem).Count == 0);
        await VerifyStep(session, Capture(session), name, "01-values-entered");

        JObject reply = SendAndWaitForZetReply(session, UpdateInterfaceConfigLine,
            () => WaitFor(session, By.XPath("//*[@Name='Save']")).Click());
        JObject sent = UiCommand(session.Relay!, UpdateInterfaceConfigLine);
        Assert.Equal(ip, (string?)sent["Data"]!["L3"]!["TunIPv4"]);
        Assert.Equal(prefixLength, (int?)sent["Data"]!["L3"]!["TunPrefixLength"]);
        return reply;
    }
}
