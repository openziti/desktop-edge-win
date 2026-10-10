using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.IntegrationHelpers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Tunnel Config's Save sends UpdateInterfaceConfig. ZET validates its L3 part with UpdateTunIPv4's checks and the UI
/// has no preflight, so every rejection is ZET's.
/// The blurb never shows ZET's reason, since DataClient turns any failed reply into one generic ServiceException.
/// </summary>
[TestLifecycleLog]
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

    public UpdateTunIpTests(IntegrationFixture fixture)
    {
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

    /// <summary>The ConfigMaskNew item for prefixLength, such as "/16 - 255.255.0.0".</summary>
    private static string MaskItemName(int prefixLength)
    {
        uint mask = uint.MaxValue << (32 - prefixLength);
        return $"/{prefixLength} - {mask >> 24}.{(mask >> 16) & 255}.{(mask >> 8) & 255}.{mask & 255}";
    }

    /// <summary>
    /// Pick prefixLength from the mask dropdown. It shows 15 of its 32 rows scrolled to the current mask, so a far row is
    /// offscreen until Home or End scrolls to its end of the list, and only 16 and 17 need to be current to be picked.
    /// </summary>
    private static void SelectMask(AppiumSession s, int prefixLength)
    {
        string jump = prefixLength <= 15 ? Keys.Home : Keys.End;
        ClickUntil(s, ById("ConfigMaskNew"), By.XPath("//ListItem[@IsOffscreen='False']"));
        // Scoped to the ComboBox, these XPaths walk its rows instead of the whole UIA tree.
        IWebElement combo = WaitForId(s, "ConfigMaskNew");
        By item = By.XPath($".//ListItem[@Name='{MaskItemName(prefixLength)}']");
        By onscreenItem = By.XPath(".//ListItem[@IsOffscreen='False']");
        IWebElement? row = null;
        // Pressed until the row shows, since WinAppDriver can drop a keystroke.
        WaitUntil(s, $"/{prefixLength} scrolls into view", TimeSpan.FromSeconds(20), () =>
        {
            row = combo.FindElements(item).FirstOrDefault(e => e.Displayed);
            if (row != null)
            {
                return true;
            }
            combo.FindElements(onscreenItem).First().SendKeys(jump);
            return false;
        });
        ClickAt(s, row!);
        WaitUntil(s, "the mask dropdown closes", TimeSpan.FromSeconds(5),
            () => combo.FindElements(onscreenItem).Count == 0);
    }

    /// <summary>The one success path of UpdateTunIPv4's checks.</summary>
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
            await using AppiumSession s = await LaunchAsync(_fixture, name);
            JObject reply = await SaveValues(s, name, ValidIp, SavedPrefixLength);
            Assert.Equal(0, (int?)reply["Code"]);
            await VerifyStep(CaptureBlurbOnReply(s, UpdateInterfaceConfigLine), name, "02-saved-blurb");
            // ZET replies before it saves config.json.
            WaitUntil(s, "ZET saves the new tun IP", TimeSpan.FromSeconds(2), () =>
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

    /// <summary>Save ip and prefixLength on Tunnel Config, asserting ZET rejects them with zetError.</summary>
    private async Task AssertSaveRejected(string name, string ip, int prefixLength, string zetError)
    {
        await using AppiumSession s = await LaunchAsync(_fixture, name);
        JObject reply = await SaveValues(s, name, ip, prefixLength);
        Assert.Equal(500, (int?)reply["Code"]);
        Assert.Equal(zetError, (string?)reply["Error"]);
        await VerifyStep(CaptureBlurbOnReply(s, UpdateInterfaceConfigLine), name, "02-rejected-blurb");
    }

    /// <summary>Enter ip and prefixLength on Tunnel Config and Save, returning ZET's reply.</summary>
    private static async Task<JObject> SaveValues(AppiumSession s, string name, string ip, int prefixLength)
    {
        OpenMainMenu(s);
        ClickUntil(s, By.XPath("//*[@Name='Advanced Settings']"), By.XPath("//*[@Name='Tunnel Config']"));
        ClickUntil(s, By.XPath("//*[@Name='Tunnel Config']"), By.XPath("//*[@Name='Edit Values']"));
        ClickUntil(s, By.XPath("//*[@Name='Edit Values']"), By.XPath("//*[@Name='Save']"));

        TypeUntil(s, ById("ConfigIpNew"), ip);
        SelectMask(s, prefixLength);
        await VerifyStep(Capture(s), name, "01-values-entered");

        JObject reply = SendAndWaitForZetReply(s, UpdateInterfaceConfigLine,
            () => WaitFor(s, By.XPath("//*[@Name='Save']")).Click());
        JObject sent = UiCommand(s.Relay!, UpdateInterfaceConfigLine);
        Assert.Equal(ip, (string?)sent["Data"]!["L3"]!["TunIPv4"]);
        Assert.Equal(prefixLength, (int?)sent["Data"]!["L3"]!["TunPrefixLength"]);
        return reply;
    }
}
