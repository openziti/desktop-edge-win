using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// Every release stamps a new assembly version, which moves user.config into a new version directory, and
/// App.OnStartup carries the previous version's values over with Settings.Upgrade.
/// </summary>
[TestLifecycleLog]
[Trait("Category", "Settings")]
public class SettingsUpgradeTests
{
    private static readonly By ProviderMenuItems = By.XPath("//MenuItem");

    [Fact(Timeout = 90000)]
    [Trait("Category", "Screenshots")]
    public async Task SortAndDefaultProviderCarriedOverFromPreviousVersion()
    {
        string name = nameof(SortAndDefaultProviderCarriedOverFromPreviousVersion);
        string exePath = DefaultExePath();
        Version current = AppUserConfig.SettingsVersion(exePath);
        // CharlieEdge is SortableMixed's only identity needing ext auth, offered keycloak and auth0.
        UserSettings previous = new UserSettings("Services", "Descending", @"c:\fake\ids\CharlieEdge.json", "auth0");
        JObject status = FixtureBuilder.SortableMixed();
        // A disabled row shows "id disabled" in place of the ext auth icon.
        status["Identities"]!.First(id => (string?)id["Name"] == "CharlieEdge")["Active"] = true;
        Dictionary<string, byte[]> originals = AppUserConfig.ReadConfigs(current);
        string? seededDir = null;
        try
        {
            foreach (string path in originals.Keys) File.Delete(path);
            // With no user.config for its version, the app saves a fresh one, which is how the test finds its directory.
            await using (AppiumSession s = await AppiumSession.LaunchAsync(exePath, status, UiLogPath(name)))
            {
                WaitForId(s, "SortByName");
                // App.config's defaults, as long as no older version folder holds a sort for Upgrade to copy.
                Assert.Equal(("Name", "▲"), ActiveSortArrow(s));
                await VerifyStep(Capture(s), name, "01-defaults");
            }
            string currentConfig = Assert.Single(AppUserConfig.ReadConfigs(current).Keys);
            File.Delete(currentConfig);
            seededDir = AppUserConfig.SeedPreviousVersion(currentConfig, current, previous);

            await using AppiumSession upgraded = await AppiumSession.LaunchAsync(exePath, status, UiLogPath(name));
            WaitForId(upgraded, "SortByName");
            Assert.Equal(("Services", "▼"), ActiveSortArrow(upgraded));
            // Services descending puts the identity needing ext auth below every connected one.
            Assert.Equal("CharlieEdge", ListedNames(upgraded).Last());
            await VerifyStep(Capture(upgraded), name, "02-upgraded-sort");

            OpenIdentityDetails(upgraded, "CharlieEdge");
            IWebElement defaultProvider = WaitFor(upgraded,
                By.XPath($"//List[@AutomationId='ProviderList']/ListItem[@Name='{previous.DefaultProvider}']"));
            Assert.True(defaultProvider.Selected, $"{previous.DefaultProvider} is not selected in Configured Providers");
            Assert.True(WaitForId(upgraded, "IsDefaultProvider").Selected, "Default provider? is not ticked");
            await VerifyStep(Capture(upgraded), name, "03-default-provider");
            CloseIdentityDetails(upgraded);

            // With a default provider the click logs in to it, where two providers and none would open a menu.
            ClickAt(upgraded, WaitFor(upgraded, ExtAuthRequiredIcon));
            JObject externalAuth = WaitForDataRequest(upgraded, "ExternalAuth");
            Assert.Equal(previous.DefaultProviderIdentity, (string?)externalAuth["Data"]!["Identifier"]);
            Assert.Equal(previous.DefaultProvider, (string?)externalAuth["Data"]!["Provider"]);
            Assert.DoesNotContain(upgraded.Driver.FindElements(ProviderMenuItems), item => item.Displayed);
        }
        finally
        {
            if (seededDir != null) Directory.Delete(seededDir, true);
            AppUserConfig.RestoreConfigs(current, originals);
        }
    }

    private static JObject WaitForDataRequest(AppiumSession s, string command)
    {
        JObject? request = null;
        WaitUntil(s, $"the app sends {command}", AppResponseTimeout, () =>
        {
            request = s.Mock.ReceivedDataRequests.FirstOrDefault(r => (string?)r["Command"] == command);
            return request != null;
        });
        return request!;
    }
}
