using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

[Trait("Category", "Settings")]
public class SettingsUpgradeTests
{
    public SettingsUpgradeTests(ITestOutputHelper output)
    {
        Step.Use(output);
    }

    [Fact(Timeout = 90000)]
    [Trait("Category", "Screenshots")]
    public async Task SortAndDefaultProviderCarriedOverFromPreviousVersion()
    {
        string name = nameof(SortAndDefaultProviderCarriedOverFromPreviousVersion);
        string exePath = DefaultExePath();
        // The app sets no informational version, so its settings directory is named for the assembly version.
        Version current = AssemblyName.GetAssemblyName(exePath).Version!;
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
            await using (AppiumSession session = await AppiumSession.LaunchAsync(exePath, status, UiLogPath(name)))
            {
                WaitForId(session, "SortByName");
                // App.config's defaults, as long as no older version folder holds a sort for Upgrade to copy.
                Assert.Equal(("Name", "▲"), ActiveSortArrow(session));
                await VerifyStep(session, Capture(session), name, "01-defaults");
            }
            string currentConfig = Assert.Single(AppUserConfig.ReadConfigs(current).Keys);
            File.Delete(currentConfig);
            // Settings.Upgrade copies from the highest version below current, so the seed goes there and no real
            // version directory can sit between them.
            Version previousVersion;
            if (current.Revision > 0)
                previousVersion = new Version(current.Major, current.Minor, current.Build, current.Revision - 1);
            else if (current.Build > 0)
                previousVersion = new Version(current.Major, current.Minor, current.Build - 1, int.MaxValue);
            else
                throw new NotSupportedException($"no previous version to seed below {current}");
            string exeDir = Path.GetDirectoryName(Path.GetDirectoryName(currentConfig)!)!;
            string versionDir = Path.Combine(exeDir, previousVersion.ToString());
            if (Directory.Exists(versionDir))
                throw new InvalidOperationException($"{versionDir} already exists: delete it, it is left from an earlier run");
            Directory.CreateDirectory(versionDir);
            seededDir = versionDir;
            new XDocument(new XElement("configuration", new XElement("userSettings",
                new XElement("ZitiDesktopEdge.Properties.Settings",
                    StringSetting("SortOption", previous.SortOption),
                    StringSetting("SortDirection", previous.SortDirection),
                    StringSetting("SettingsUpgradeRequired", "False"),
                    new XElement("setting", new XAttribute("name", "DefaultProviders"), new XAttribute("serializeAs", "Xml"),
                        new XElement("value", new XElement("ArrayOfString", new XElement("string",
                            previous.DefaultProviderIdentity + AppUserConfig.ProviderDelimiter + previous.DefaultProvider))))))))
                .Save(Path.Combine(versionDir, "user.config"));

            await using AppiumSession upgraded = await AppiumSession.LaunchAsync(exePath, status, UiLogPath(name));
            WaitForId(upgraded, "SortByName");
            Assert.Equal(("Services", "▼"), ActiveSortArrow(upgraded));
            // Services descending puts the identity needing ext auth below every connected one.
            Assert.Equal("CharlieEdge", ListedNames(upgraded).Last());
            await VerifyStep(upgraded, Capture(upgraded), name, "02-upgraded-sort");

            OpenIdentityDetails(upgraded, "CharlieEdge");
            IWebElement defaultProvider = WaitFor(upgraded,
                By.XPath($"//List[@AutomationId='ProviderList']/ListItem[@Name='{previous.DefaultProvider}']"));
            Assert.True(defaultProvider.Selected, $"{previous.DefaultProvider} is not selected in Configured Providers");
            Assert.True(WaitForId(upgraded, "IsDefaultProvider").Selected, "Default provider? is not ticked");
            await VerifyStep(upgraded, Capture(upgraded), name, "03-default-provider");
            CloseIdentityDetails(upgraded);

            // With a default provider the click logs in to it, where two providers and none would open a menu.
            ClickAt(upgraded, WaitFor(upgraded, ExtAuthRequiredIcon));
            JObject? externalAuth = null;
            WaitUntil(upgraded, "the app sends ExternalAuth", AppResponseTimeout, () =>
            {
                externalAuth = upgraded.Mock.ReceivedDataRequests
                    .FirstOrDefault(request => (string?)request["Command"] == "ExternalAuth");
                return externalAuth != null;
            });
            Assert.Equal(previous.DefaultProviderIdentity, (string?)externalAuth!["Data"]!["Identifier"]);
            Assert.Equal(previous.DefaultProvider, (string?)externalAuth["Data"]!["Provider"]);
        }
        finally
        {
            if (seededDir != null) Directory.Delete(seededDir, true);
            foreach (string path in AppUserConfig.ReadConfigs(current).Keys) File.Delete(path);
            foreach ((string path, byte[] bytes) in originals) File.WriteAllBytes(path, bytes);
        }
    }

    private static XElement StringSetting(string name, string value) =>
        new XElement("setting", new XAttribute("name", name), new XAttribute("serializeAs", "String"),
            new XElement("value", value));
}
