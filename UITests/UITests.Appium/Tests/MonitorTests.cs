using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

// The monitor's settings.json is the test user's own, so every test leaves it as it found it.
public class MonitorTests
{
    public MonitorTests(ITestOutputHelper output)
    {
        Step.Use(output);
    }

    // The monitor fetches the release stream more than once before it answers.
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(15);
    // Nothing listens here, and Windows retries a refused connect, so the refusal still takes about 2s.
    private const string UnreachableUrl = "http://127.0.0.1:47381/stable.json";
    private const string PolicyKeyPath = @"SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service";
    private static readonly By PolicyBanner = By.XPath("//*[contains(@Name, 'Managed by your organization')]");

    private enum Frequency { Daily = 0, Weekly = 1, Monthly = 2 }

    private enum MonthlyMode { ByDate = 0, ByWeekday = 1 }

    private sealed record MaintenanceWindow(int? Start, int? End, Frequency Frequency, int? DayOfWeek, int? DayOfMonth,
        MonthlyMode MonthlyMode, int? MonthlyOrdinal)
    {
        public static MaintenanceWindow From(JObject settings) => new MaintenanceWindow(
            (int?)settings["MaintenanceWindowStart"], (int?)settings["MaintenanceWindowEnd"],
            (Frequency)(int)settings["MaintenanceWindowFrequency"]!, (int?)settings["MaintenanceWindowDayOfWeek"],
            (int?)settings["MaintenanceWindowDayOfMonth"], (MonthlyMode)(int)settings["MaintenanceWindowMonthlyMode"]!,
            (int?)settings["MaintenanceWindowMonthlyOrdinal"]);

        public JObject ToSettings(string? updateUrl) => new JObject
        {
            ["AutomaticUpdateURL"] = updateUrl,
            ["MaintenanceWindowStart"] = Start,
            ["MaintenanceWindowEnd"] = End,
            ["MaintenanceWindowFrequency"] = (int)Frequency,
            ["MaintenanceWindowDayOfWeek"] = DayOfWeek,
            ["MaintenanceWindowDayOfMonth"] = DayOfMonth,
            ["MaintenanceWindowMonthlyMode"] = (int)MonthlyMode,
            ["MaintenanceWindowMonthlyOrdinal"] = MonthlyOrdinal,
        };
    }

    [Fact(Timeout = 90000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task AutomaticUpgradesToggleSavedByMonitor()
    {
        string name = nameof(AutomaticUpgradesToggleSavedByMonitor);
        // No file means the monitor starts from defaults, so automatic upgrades start enabled.
        await WithSettings(null, async () =>
        {
            await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
            // The label starts as DISABLED in XAML, so any other text can only come from the monitor's status event.
            WaitForText(session, "AutomaticUpgradesToggleLabel", "ENABLED", AppResponseTimeout);
            await VerifyStep(session, Capture(session), name, "01-automatic-upgrades-enabled");

            ClickAt(session, WaitForId(session, "AutomaticUpgradesToggle"));
            WaitForBlurb(session, "Automatic upgrades DISABLED", AppResponseTimeout);
            WaitForSaved(session, "AutomaticUpdatesDisabled", true);
            WaitForText(session, "AutomaticUpgradesToggleLabel", "DISABLED", AppResponseTimeout);

            ClickAt(session, WaitForId(session, "AutomaticUpgradesToggle"));
            WaitForSaved(session, "AutomaticUpdatesDisabled", false);
            WaitForText(session, "AutomaticUpgradesToggleLabel", "ENABLED", AppResponseTimeout);
        });
    }

    [Fact(Timeout = 90000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task MaintenanceWindowSavedByMonitor()
    {
        string name = nameof(MaintenanceWindowSavedByMonitor);
        await using ReleaseStreamServer releaseStream = ReleaseStreamServer.Start(
            Fixture("release-stream-older.json").ToString());
        MaintenanceWindow seeded = new MaintenanceWindow(1, 4, Frequency.Daily, null, null, MonthlyMode.ByDate, null);
        byte[] settings = Encoding.UTF8.GetBytes(seeded.ToSettings(releaseStream.Url).ToString());
        await WithSettings(settings, async () =>
        {
            await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
            // The combos start at index 0, so 01:00 can only come from the monitor's status event.
            WaitForText(session, "MaintenanceWindowStartCombo", "01:00", AppResponseTimeout);
            Assert.Equal("04:00", TextById(session, "MaintenanceWindowEndCombo"));
            Assert.Equal("Daily", TextById(session, "MaintenanceWindowFrequencyCombo"));
            Assert.Equal(releaseStream.Url, TextById(session, "UpdateUrl"));
            await VerifyStep(session, Capture(session), name, "01-seeded-window");

            SelectComboItem(session, "MaintenanceWindowFrequencyCombo", "Weekly");
            SelectComboItem(session, "MaintenanceWindowDayOfWeekCombo", "Wednesday");
            SelectComboItem(session, "MaintenanceWindowStartCombo", "02:00");
            SelectComboItem(session, "MaintenanceWindowEndCombo", "05:00");
            ClickAt(session, WaitForId(session, "SaveSettingsButton"));

            // Captured before the asserts below, which take long enough that the blurb hides first.
            // The monitor pipes have no relay, so ui.log is the only record of the reply.
            string uiLogPath = UiLogPath(name);
            DateTime? savedAtUtc = null;
            WaitUntil(session, "the app reads the monitor's SetMaintenanceWindow reply", AppResponseTimeout, () =>
            {
                string[] lines;
                using (FileStream uiLog = new FileStream(uiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(uiLog))
                    lines = reader.ReadToEnd().Split('\n');
                int sent = Array.FindLastIndex(lines, line => line.Contains("UI-MonitorClient-send-monitor:")
                    && line.Contains("\"Op\":\"SetMaintenanceWindow\""));
                if (sent < 0) return false;
                int reply = Array.FindIndex(lines, sent + 1, line => line.Contains("UI-MonitorClient-read-monitor:"));
                if (reply < 0) return false;
                // The layout depends on whether the machine has the app installed. Both are local time, the Z included.
                Match configured = Regex.Match(lines[reply], @"^\[(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3})Z\]");
                Match nlogDefault = Regex.Match(lines[reply], @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4})\|");
                DateTime replyAtLocal;
                if (configured.Success)
                    replyAtLocal = DateTime.ParseExact(configured.Groups[1].Value, "yyyy-MM-ddTHH:mm:ss.fff",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                else if (nlogDefault.Success)
                    replyAtLocal = DateTime.ParseExact(nlogDefault.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.ffff",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                else
                    throw new FormatException(
                        $"ui.log line has no timestamp in a known layout: '{lines[reply]}' in {uiLogPath}");
                savedAtUtc = replyAtLocal.ToUniversalTime();
                return true;
            });
            await VerifyStep(session, CaptureBlurb(session, savedAtUtc!.Value), name, "02-saved-blurb");
            MaintenanceWindow? saved = null;
            WaitUntil(session, $"the monitor saves a maintenance window other than {seeded}", AppResponseTimeout, () =>
            {
                JObject? savedSettings = SavedSettings();
                if (savedSettings == null)
                    return false;
                saved = MaintenanceWindow.From(savedSettings);
                return saved != seeded;
            });
            Assert.Equal(new MaintenanceWindow(2, 5, Frequency.Weekly, 3, null, MonthlyMode.ByDate, null), saved);
            // Save Settings sends the URL too, and the monitor fetches it before saving it.
            Assert.Equal(1, releaseStream.Requests);
            Assert.Equal(releaseStream.Url, (string?)SavedSettings()?["AutomaticUpdateURL"]);
        });
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task UpdateRequestScheduledForMaintenanceWindow()
    {
        string name = nameof(UpdateRequestScheduledForMaintenanceWindow);
        await using ReleaseStreamServer releaseStream = ReleaseStreamServer.Start(
            Fixture("release-stream-newer.json").ToString());
        // Twelve hours away, so the window never holds while the test runs and a request is scheduled, never installed.
        int start = (DateTime.Now.Hour + 12) % 24;
        MaintenanceWindow seeded = new MaintenanceWindow(start, (start + 1) % 24, Frequency.Daily, null, null,
            MonthlyMode.ByDate, null);
        byte[] settings = Encoding.UTF8.GetBytes(seeded.ToSettings(releaseStream.Url).ToString());
        await WithSettings(settings, async () =>
        {
            await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
            // The window hours follow the time of day the test runs.
            By[] windowCombos = { ById("MaintenanceWindowStartCombo"), ById("MaintenanceWindowEndCombo") };
            ClickUntil(session, ById("CheckForUpdate"), "the update check starts",
                () => TextById(session, "CheckForUpdateStatus") != "");
            WaitForText(session, "CheckForUpdateStatus", "An update is available: 99.0.0.0", UpdateCheckTimeout);
            WaitForId(session, "TriggerUpdateButton");
            await VerifyStep(session, Masked(session, Capture(session), windowCombos), name, "01-update-offered");

            ClickUntil(session, ById("TriggerUpdateButton"), "the update request starts",
                () => TextById(session, "CheckForUpdateStatus") != "An update is available: 99.0.0.0");
            WaitForText(session, "CheckForUpdateStatus", "Update scheduled for maintenance window", UpdateCheckTimeout);
            await VerifyStep(session, Masked(session, Capture(session), windowCombos), name, "02-update-now-clicked-outside-window");

            // BackArrow is a StackPanel, which has no automation peer, so the click goes to its label.
            By back = By.XPath("//*[@Name='Back']");
            ClickUntil(session, back, By.XPath("//*[@Name='Configure Automatic Upgrades']"));
            ClickUntil(session, back, ById("ForceUpdate"));
            Assert.Equal("Update scheduled for maintenance window", TextById(session, "UpdateTimeLeft"));
            Assert.Equal("Update Now", TextById(session, "ForceUpdate"));
            await VerifyStep(session, Capture(session), name, "03-main-menu-update-scheduled");
        });
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task BadUpdateUrlRejectedBySave()
    {
        string name = nameof(BadUpdateUrlRejectedBySave);
        await using ReleaseStreamServer releaseStream = ReleaseStreamServer.Start(
            Fixture("release-stream-older.json").ToString());
        byte[] settings = Encoding.UTF8.GetBytes(new JObject { ["AutomaticUpdateURL"] = releaseStream.Url }.ToString());
        await WithSettings(settings, async () =>
        {
            await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
            WaitForText(session, "UpdateUrl", releaseStream.Url, AppResponseTimeout);

            // UIA reports the monitor's \n line breaks as \r\n.
            Assert.Equal("The url supplied is invalid: \r\nnot-a-url\r\n", SaveRejectedUrl(session, releaseStream, "not-a-url"));
            await VerifyStep(session, Capture(session), name, "01-not-a-url-rejected");
            CloseError(session, releaseStream);

            Assert.StartsWith("Unable to connect to the remote server:", SaveRejectedUrl(session, releaseStream, UnreachableUrl));
            await VerifyStep(session, Capture(session), name, "02-unreachable-url-rejected");
            CloseError(session, releaseStream);

            // Only typed into the box, never saved, since saving it would fetch the production stream.
            ClickAt(session, WaitForId(session, "ResetUrlButton"));
            WaitForText(session, "UpdateUrl", "https://get.openziti.io/zdew/stable.json", AppResponseTimeout);
            await VerifyStep(session, Capture(session), name, "03-reset-to-default-url");
        });
    }

    [Fact(Timeout = 90000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task UpdateCheckFailsOnUnreachableUrl()
    {
        string name = nameof(UpdateCheckFailsOnUnreachableUrl);
        byte[] settings = Encoding.UTF8.GetBytes(new JObject { ["AutomaticUpdateURL"] = UnreachableUrl }.ToString());
        await WithSettings(settings, async () =>
        {
            await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
            WaitForText(session, "UpdateUrl", UnreachableUrl, AppResponseTimeout);
            ClickUntil(session, ById("CheckForUpdate"), "the update check starts",
                () => TextById(session, "CheckForUpdateStatus") != "");
            WaitForText(session, "CheckForUpdateStatus", "FAILURE: Unable to connect to the remote server",
                UpdateCheckTimeout);
            Assert.Null(FindByAccessibilityId(session, "TriggerUpdateButton"));
            await VerifyStep(session, Capture(session), name, "01-check-failed");
        });
    }

    [Fact(Timeout = 120000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task PolicyLocksAutomaticUpgradesUntilRemoved()
    {
        string name = nameof(PolicyLocksAutomaticUpgradesUntilRemoved);
        await using ReleaseStreamServer releaseStream = ReleaseStreamServer.Start(
            Fixture("release-stream-older.json").ToString());
        // Different from the policy's values, so the screen shows which one wins.
        MaintenanceWindow local = new MaintenanceWindow(1, 4, Frequency.Daily, null, null, MonthlyMode.ByDate, null);
        byte[] settings = Encoding.UTF8.GetBytes(local.ToSettings(UnreachableUrl).ToString());
        await WithSettings(settings, async () =>
        {
            // Writing under HKLM needs an elevated run.
            if (KeyExists(PolicyKeyPath))
                throw new InvalidOperationException($@"HKLM\{PolicyKeyPath} already exists, so it may be a real policy");
            string topCreated = PolicyKeyPath;
            while (!KeyExists(topCreated[..topCreated.LastIndexOf('\\')]))
                topCreated = topCreated[..topCreated.LastIndexOf('\\')];
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(PolicyKeyPath))
            {
                key.SetValue("AutomaticUpdateURL", releaseStream.Url);
                key.SetValue("MaintenanceWindowStart", 2);
                key.SetValue("MaintenanceWindowEnd", 5);
            }
            try
            {
                await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
                WaitFor(session, PolicyBanner);
                WaitForText(session, "UpdateUrl", releaseStream.Url, AppResponseTimeout);
                Assert.Equal("02:00", TextById(session, "MaintenanceWindowStartCombo"));
                Assert.Equal("05:00", TextById(session, "MaintenanceWindowEndCombo"));
                Assert.False(WaitForId(session, "AutomaticUpgradesToggle").Enabled);
                Assert.False(WaitForId(session, "UpdateUrl").Enabled);
                Assert.False(WaitForId(session, "MaintenanceWindowStartCombo").Enabled);
                Assert.Null(FindByAccessibilityId(session, "ResetUrlButton"));
                Assert.Null(FindByAccessibilityId(session, "SaveSettingsButton"));
                await VerifyStep(session, Capture(session), name, "01-locked-by-policy");

                Registry.LocalMachine.DeleteSubKey(PolicyKeyPath);
                // The monitor reloads policy 500ms after the last registry change WMI reports.
                WaitForText(session, "UpdateUrl", UnreachableUrl, TimeSpan.FromSeconds(10));
                WaitForGone(session, PolicyBanner);
                Assert.Equal("01:00", TextById(session, "MaintenanceWindowStartCombo"));
                Assert.Equal("04:00", TextById(session, "MaintenanceWindowEndCombo"));
                Assert.True(WaitForId(session, "AutomaticUpgradesToggle").Enabled);
                Assert.True(WaitForId(session, "UpdateUrl").Enabled);
                Assert.True(WaitForId(session, "MaintenanceWindowStartCombo").Enabled);
                WaitForId(session, "SaveSettingsButton");
                await VerifyStep(session, Capture(session), name, "02-policy-removed");
            }
            finally
            {
                // The test may have deleted the policy key itself.
                Registry.LocalMachine.DeleteSubKeyTree(topCreated, throwOnMissingSubKey: false);
            }
        });
    }

    [Fact(Timeout = 150000)]
    [Trait("Category", "Monitor")]
    [Trait("Category", "Screenshots")]
    public async Task SettingsRestoredFromWindowsOldByMonitor()
    {
        string name = nameof(SettingsRestoredFromWindowsOldByMonitor);
        await using ReleaseStreamServer releaseStream = ReleaseStreamServer.Start(
            Fixture("release-stream-older.json").ToString());
        MaintenanceWindow backedUp = new MaintenanceWindow(3, 6, Frequency.Weekly, 3, null, MonthlyMode.ByDate, null);
        JObject backup = backedUp.ToSettings(releaseStream.Url);
        // The same path the monitor's restore reads.
        string liveFolder = Path.GetDirectoryName(MonitorProcess.SettingsPath())!;
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        string backupFolder = Path.Combine(systemDrive, "Windows.old", liveFolder.Substring(Path.GetPathRoot(liveFolder)!.Length));
        await WithSettings(null, async () =>
        {
            if (Directory.Exists(backupFolder))
                throw new InvalidOperationException($"{backupFolder} already exists, so it may hold a real upgrade backup");
            List<string> created = new List<string>();
            for (string? folder = backupFolder; folder != null && !Directory.Exists(folder);
                 folder = Path.GetDirectoryName(folder))
                created.Add(folder);
            string backupFile = Path.Combine(backupFolder, "settings.json");
            Directory.CreateDirectory(backupFolder);
            File.WriteAllBytes(backupFile, Encoding.UTF8.GetBytes(backup.ToString()));
            try
            {
                await using AppiumSession session = await LaunchOnAutomaticUpgrades(name);
                // The combos start at index 0, so 03:00 can only come from the restored file.
                WaitForText(session, "MaintenanceWindowStartCombo", "03:00", AppResponseTimeout);
                Assert.Equal("06:00", TextById(session, "MaintenanceWindowEndCombo"));
                Assert.Equal("Weekly", TextById(session, "MaintenanceWindowFrequencyCombo"));
                Assert.Equal("Wednesday", TextById(session, "MaintenanceWindowDayOfWeekCombo"));
                Assert.Equal(releaseStream.Url, TextById(session, "UpdateUrl"));
                Assert.Equal("ENABLED", TextById(session, "AutomaticUpgradesToggleLabel"));
                await VerifyStep(session, Capture(session), name, "02-restored");
                JObject restored = SavedSettings()!;
                Assert.Equal(backedUp, MaintenanceWindow.From(restored));
                Assert.Equal(releaseStream.Url, (string?)restored["AutomaticUpdateURL"]);
                Assert.False(Directory.Exists(backupFolder));
            }
            finally
            {
                if (File.Exists(backupFile))
                    File.Delete(backupFile);
                // Deepest first, and the monitor removes backupFolder itself once it has restored the file.
                foreach (string folder in created.Where(Directory.Exists))
                    Directory.Delete(folder);
            }
        });
    }

    [Fact(Timeout = 180000)]
    [Trait("Category", "Monitor")]
    public async Task FeedbackBundleWrittenByMonitor()
    {
        string name = nameof(FeedbackBundleWrittenByMonitor);
        await WithSettings(null, async () =>
        {
            string monitorLogs = Path.Combine(Path.GetDirectoryName(DefaultMonitorPath())!, "logs");
            IReadOnlySet<string> bundlesBefore = Bundles(monitorLogs);
            IReadOnlySet<IntPtr> explorerBefore = ExplorerWindows.Open().Select(window => window.Handle).ToHashSet();
            await using AppiumSession session = await LaunchOnMainMenu(name);
            try
            {
                ClickAt(session, WaitFor(session, By.XPath("//*[@Name='Feedback']")));
                WaitFor(session, By.XPath("//*[@Name='Collecting Information']"));
                // The app opens Explorer on the bundle only once the monitor has answered, so the zip is complete by then.
                // The monitor runs systeminfo, netstat and the other system info commands one after another.
                TopLevelWindow? explorer = null;
                WaitUntil(session, "the app opens an Explorer window", TimeSpan.FromSeconds(120), () =>
                {
                    explorer = ExplorerWindows.Open().FirstOrDefault(window => !explorerBefore.Contains(window.Handle));
                    return explorer != null;
                });
                Step.Log($"the app opened Explorer window '{explorer!.Title}'");
                ExplorerWindows.Quit(explorer, AppResponseTimeout);
                // Explorer can keep a window open for more than 5s after Quit.
                WaitUntil(session, "the Explorer window closes", TimeSpan.FromSeconds(30), () => !Win32Window.IsWindow(explorer.Handle));
                string bundle = Assert.Single(Bundles(monitorLogs).Except(bundlesBefore));
                try
                {
                    WaitForGone(session, By.XPath("//*[@Name='Collecting Information']"));
                    JObject dumpRequest = Assert.Single(session.Mock.ReceivedDataRequests, request => (string?)request["Command"] == "ZitiDump");
                    Assert.Equal(Path.Combine(Path.GetDirectoryName(DefaultExePath())!, "logs", "service"),
                        (string?)dumpRequest["Data"]?["DumpPath"]);
                    using ZipArchive bundleArchive = ZipFile.OpenRead(bundle);
                    HashSet<string> entries = bundleArchive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet();
                    Assert.Subset(entries, new HashSet<string>
                    {
                        "ZitiMonitorService/ZitiUpdateService.log", "ipconfig.all.txt", "systeminfo.txt", "dnsCache.txt",
                        "externalIP.txt", "tasklist.txt", "network-routes.txt", "netstat.txt", "NrptRule.txt",
                        "NrptPolicy.txt",
                    });
                }
                finally
                {
                    // It holds the machine's system info.
                    File.Delete(bundle);
                }
            }
            finally
            {
                foreach (TopLevelWindow leftOpen in ExplorerWindows.Open().Where(window => !explorerBefore.Contains(window.Handle)))
                    ExplorerWindows.Quit(leftOpen, AppResponseTimeout);
            }
        });
    }

    private static IReadOnlySet<string> Bundles(string monitorLogs)
    {
        if (!Directory.Exists(monitorLogs))
            return new HashSet<string>();
        return Directory.GetFiles(monitorLogs, "*.zip", SearchOption.TopDirectoryOnly).ToHashSet();
    }

    private static string SaveRejectedUrl(AppiumSession session, ReleaseStreamServer releaseStream, string url)
    {
        TypeUntil(session, ById("UpdateUrl"), url);
        ClickAt(session, WaitForId(session, "SaveSettingsButton"));
        // The monitor checks the URL by fetching it before it answers, so this waits as long as an update check.
        WaitFor(session, ErrorTitle("Error Saving Settings"), UpdateCheckTimeout);
        string details = TextById(session, "ErrorDetails");
        Assert.Equal(releaseStream.Url, (string?)SavedSettings()?["AutomaticUpdateURL"]);
        return details;
    }

    private static void CloseError(AppiumSession session, ReleaseStreamServer releaseStream)
    {
        ClickUntilGone(session, CloseErrorButton);
        WaitForText(session, "UpdateUrl", releaseStream.Url, AppResponseTimeout);
    }

    private static async Task<AppiumSession> LaunchOnMainMenu(string name)
    {
        AppiumSession session = await AppiumSession.LaunchAgainstMonitorAsync(DefaultExePath(),
            DefaultMonitorPath(), Fixture("landing-status.json"), UiLogPath(name),
            Path.Combine(TestResultsDir(), "logs", name, "monitor.log"));
        return await PrepareOrDispose(session, launched =>
        {
            WaitForId(launched, "ConnectLabel");
            PrepareTestWindow(launched);
            OpenMainMenu(launched);
        });
    }

    private static async Task<AppiumSession> LaunchOnAutomaticUpgrades(string name)
    {
        AppiumSession session = await LaunchOnMainMenu(name);
        return await PrepareOrDispose(session, launched =>
        {
            ClickUntil(launched, By.XPath("//*[@Name='Advanced Settings']"),
                By.XPath("//*[@Name='Configure Automatic Upgrades']"));
            ClickUntil(launched, By.XPath("//*[@Name='Configure Automatic Upgrades']"), ById("AutomaticUpgradesToggle"));
        });
    }

    private static void SelectComboItem(AppiumSession session, string comboId, string item)
    {
        // Scoped to the combo, so another combo's item with the same name never matches, and the search stays fast.
        IWebElement combo = WaitForId(session, comboId);
        By onscreenItem = By.XPath(".//ListItem[@IsOffscreen='False']");
        By row = By.XPath($".//ListItem[@Name='{item}']");
        IWebElement? shown = null;
        ClickUntil(session, ById(comboId), $"{comboId} lists {item}", () =>
        {
            shown = combo.FindElements(row).FirstOrDefault(element => element.Displayed);
            return shown != null;
        });
        ClickAt(session, shown!);
        WaitUntil(session, $"{comboId} closes", AppResponseTimeout,
            () => combo.FindElements(onscreenItem).Count == 0);
        WaitForText(session, comboId, item, AppResponseTimeout);
    }

    private static void WaitForSaved(AppiumSession session, string key, bool expected) =>
        WaitUntil(session, $"the monitor saves {key} {expected}", AppResponseTimeout,
            () => (bool?)SavedSettings()?[key] == expected);

    // The put back runs after test disposes its monitor, so nothing overwrites it.
    private static async Task WithSettings(byte[]? start, Func<Task> test)
    {
        string path = MonitorProcess.SettingsPath();
        byte[]? original;
        if (File.Exists(path))
            original = File.ReadAllBytes(path);
        else
            original = null;
        WriteSettings(start);
        try
        {
            await test();
        }
        finally
        {
            WriteSettings(original);
        }
    }

    private static bool KeyExists(string path)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
        return key != null;
    }

    private static void WriteSettings(byte[]? content)
    {
        string path = MonitorProcess.SettingsPath();
        if (content == null)
        {
            // File.Delete throws on a missing directory, and only a started monitor creates it.
            if (File.Exists(path))
                File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // Null while the file is missing or mid-write.
    private static JObject? SavedSettings()
    {
        string path = MonitorProcess.SettingsPath();
        try
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new StreamReader(stream);
            return JObject.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonReaderException)
        {
            return null;
        }
    }
}
