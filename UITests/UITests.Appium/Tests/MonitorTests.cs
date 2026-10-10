using System.IO.Compression;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using ZitiDesktopEdge.UITests.Drivers;
using static ZitiDesktopEdge.UITests.Tests.TestHelpers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// The app against the real monitor with a mock ZET. The monitor's settings.json is the test user's own, so every test
/// leaves it as it found it.
/// </summary>
[TestLifecycleLog]
public class MonitorTests
{
    private static readonly TimeSpan SettingsWriteTimeout = TimeSpan.FromSeconds(5);
    // The monitor fetches the release stream more than once before it answers.
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(15);
    // Next to ReleaseStreamServer's port, with nothing listening on it. Windows retries a refused connect, so the
    // refusal still takes about 2s.
    private const string UnreachableUrl = "http://127.0.0.1:47381/stable.json";
    // The monitor runs systeminfo, netstat and the other system info commands one after another.
    private static readonly TimeSpan FeedbackTimeout = TimeSpan.FromSeconds(120);
    // The monitor reloads policy 500ms after the last registry change WMI reports.
    private static readonly TimeSpan PolicyReloadTimeout = TimeSpan.FromSeconds(10);
    private const string PolicyKeyPath = @"SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service";
    private static readonly By PolicyBanner = By.XPath("//*[contains(@Name, 'Managed by your organization')]");

    private enum Frequency { Daily = 0, Weekly = 1, Monthly = 2 }

    private enum MonthlyMode { ByDate = 0, ByWeekday = 1 }

    /// <summary>The maintenance window fields of the monitor's settings.json.</summary>
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
        await WithSettings(null, () => ToggleAutomaticUpgrades(name));
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
        await WithSettings(settings, () => SaveMaintenanceWindow(name, releaseStream, seeded));
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
        await WithSettings(settings, () => RequestUpdate(name));
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
        await WithSettings(settings, () => SaveBadUpdateUrls(name, releaseStream));
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
            await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
            WaitForText(s, "UpdateUrl", UnreachableUrl, SettingsWriteTimeout);
            ClickUntil(s, ById("CheckForUpdate"), "the update check starts",
                () => TextById(s, "CheckForUpdateStatus") != "");
            WaitForText(s, "CheckForUpdateStatus", "FAILURE: Unable to connect to the remote server",
                UpdateCheckTimeout);
            Assert.Null(FindByAccessibilityId(s, "TriggerUpdateButton"));
            await VerifyStep(Capture(s), name, "01-check-failed");
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
        Dictionary<string, object> policy = new Dictionary<string, object>
        {
            ["AutomaticUpdateURL"] = releaseStream.Url,
            ["MaintenanceWindowStart"] = 2,
            ["MaintenanceWindowEnd"] = 5,
        };
        await WithSettings(settings, () => WithPolicy(policy, () => ShowPolicyLock(name, releaseStream)));
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
        string backupFolder = WindowsOldFolder(Path.GetDirectoryName(MonitorProcess.SettingsPath())!);
        await WithSettings(null, async () =>
        {
            await using (AppiumSession s = await LaunchOnAutomaticUpgrades(name))
            {
                Assert.Equal("ENABLED", WaitForLabelChange(s, "DISABLED"));
                await VerifyStep(Capture(s), name, "01-defaults");
            }
            // The monitor writes settings.json on every start, and it restores only a file missing from the live folder.
            WriteSettings(null);
            await WithBackup(backupFolder, Encoding.UTF8.GetBytes(backup.ToString()), async () =>
            {
                await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
                // The combos start at index 0, so 03:00 can only come from the restored file.
                WaitForText(s, "MaintenanceWindowStartCombo", "03:00", SettingsWriteTimeout);
                Assert.Equal("06:00", TextById(s, "MaintenanceWindowEndCombo"));
                Assert.Equal("Weekly", TextById(s, "MaintenanceWindowFrequencyCombo"));
                Assert.Equal("Wednesday", TextById(s, "MaintenanceWindowDayOfWeekCombo"));
                Assert.Equal(releaseStream.Url, TextById(s, "UpdateUrl"));
                Assert.Equal("ENABLED", TextById(s, "AutomaticUpgradesToggleLabel"));
                await VerifyStep(Capture(s), name, "02-restored");
                JObject restored = SavedSettings()!;
                Assert.Equal(backedUp, MaintenanceWindow.From(restored));
                Assert.Equal(releaseStream.Url, (string?)restored["AutomaticUpdateURL"]);
                Assert.False(Directory.Exists(backupFolder));
            });
        });
    }

    [Fact(Timeout = 180000)]
    [Trait("Category", "Monitor")]
    public async Task FeedbackBundleWrittenByMonitor()
    {
        string name = nameof(FeedbackBundleWrittenByMonitor);
        string monitorLogs = Path.Combine(Path.GetDirectoryName(DefaultMonitorPath())!, "logs");
        IReadOnlySet<string> bundlesBefore = Bundles(monitorLogs);
        IReadOnlySet<IntPtr> explorerBefore = ExplorerWindows.Open().Select(w => w.Handle).ToHashSet();
        await using AppiumSession s = await LaunchOnMainMenu(name);

        ClickAt(s, WaitFor(s, By.XPath("//*[@Name='Feedback']")));
        WaitFor(s, By.XPath("//*[@Name='Collecting Information']"));
        // The app opens Explorer on the bundle only once the monitor has answered, so the zip is complete by then.
        TopLevelWindow? explorer = null;
        WaitUntil(s, "the app opens an Explorer window", FeedbackTimeout, () =>
        {
            explorer = ExplorerWindows.Open().FirstOrDefault(w => !explorerBefore.Contains(w.Handle));
            return explorer != null;
        });
        Step.Log($"the app opened Explorer window '{explorer!.Title}'");
        ExplorerWindows.Close(explorer, SettingsWriteTimeout);
        string bundle = Assert.Single(Bundles(monitorLogs).Except(bundlesBefore));
        try
        {
            WaitForGone(s, By.XPath("//*[@Name='Collecting Information']"));
            JObject dump = Assert.Single(s.Mock.ReceivedDataRequests, r => (string?)r["Command"] == "ZitiDump");
            Assert.Equal(Path.Combine(Path.GetDirectoryName(DefaultExePath())!, "logs", "service"),
                (string?)dump["Data"]?["DumpPath"]);
            using ZipArchive zip = ZipFile.OpenRead(bundle);
            HashSet<string> entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
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

    /// <summary>The feedback zips the monitor has written to its logs folder.</summary>
    private static IReadOnlySet<string> Bundles(string monitorLogs) =>
        Directory.Exists(monitorLogs)
            ? Directory.GetFiles(monitorLogs, "*.zip", SearchOption.TopDirectoryOnly).ToHashSet()
            : new HashSet<string>();

    private static async Task ToggleAutomaticUpgrades(string name)
    {
        await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
        // The label starts as DISABLED in XAML, so any other text can only come from the monitor's status event.
        Assert.Equal("ENABLED", WaitForLabelChange(s, "DISABLED"));
        await VerifyStep(Capture(s), name, "01-automatic-upgrades-enabled");

        ClickAt(s, WaitForId(s, "AutomaticUpgradesToggle"));
        WaitForBlurb(s, "Automatic upgrades DISABLED", SettingsWriteTimeout);
        Assert.True(WaitForSavedChange(s, null));
        Assert.Equal("DISABLED", WaitForLabelChange(s, "ENABLED"));

        ClickAt(s, WaitForId(s, "AutomaticUpgradesToggle"));
        Assert.False(WaitForSavedChange(s, true));
        Assert.Equal("ENABLED", WaitForLabelChange(s, "DISABLED"));
    }

    private static async Task SaveMaintenanceWindow(string name, ReleaseStreamServer releaseStream,
        MaintenanceWindow seeded)
    {
        await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
        // The combos start at index 0, so 01:00 can only come from the monitor's status event.
        WaitForText(s, "MaintenanceWindowStartCombo", "01:00", SettingsWriteTimeout);
        Assert.Equal("04:00", TextById(s, "MaintenanceWindowEndCombo"));
        Assert.Equal("Daily", TextById(s, "MaintenanceWindowFrequencyCombo"));
        Assert.Equal(releaseStream.Url, TextById(s, "UpdateUrl"));
        await VerifyStep(Capture(s), name, "01-seeded-window");

        SelectComboItem(s, "MaintenanceWindowFrequencyCombo", "Weekly");
        SelectComboItem(s, "MaintenanceWindowDayOfWeekCombo", "Wednesday");
        SelectComboItem(s, "MaintenanceWindowStartCombo", "02:00");
        SelectComboItem(s, "MaintenanceWindowEndCombo", "05:00");
        ClickAt(s, WaitForId(s, "SaveSettingsButton"));

        WaitForBlurb(s, "Settings Saved.", SettingsWriteTimeout);
        DateTime savedAtUtc = MonitorReplyAtUtc(name, "SetMaintenanceWindow");
        MaintenanceWindow saved = WaitForSavedWindowChange(s, seeded);
        Assert.Equal(new MaintenanceWindow(2, 5, Frequency.Weekly, 3, null, MonthlyMode.ByDate, null), saved);
        // Save Settings sends the URL too, and the monitor fetches it before saving it.
        Assert.Equal(1, releaseStream.Requests);
        Assert.Equal(releaseStream.Url, (string?)SavedSettings()?["AutomaticUpdateURL"]);
        await VerifyStep(CaptureBlurb(s, savedAtUtc), name, "02-saved-blurb");
    }

    private static async Task RequestUpdate(string name)
    {
        await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
        // The window hours follow the time of day the test runs.
        By[] windowCombos = { ById("MaintenanceWindowStartCombo"), ById("MaintenanceWindowEndCombo") };
        ClickUntil(s, ById("CheckForUpdate"), "the update check starts",
            () => TextById(s, "CheckForUpdateStatus") != "");
        WaitForText(s, "CheckForUpdateStatus", "An update is available: 99.0.0.0", UpdateCheckTimeout);
        WaitForId(s, "TriggerUpdateButton");
        await VerifyStep(Masked(s, Capture(s), windowCombos), name, "01-update-offered");

        ClickAt(s, WaitForId(s, "TriggerUpdateButton"));
        WaitForText(s, "CheckForUpdateStatus", "Update scheduled for maintenance window", UpdateCheckTimeout);
        await VerifyStep(Masked(s, Capture(s), windowCombos), name, "02-update-now-clicked-outside-window");

        // BackArrow is a StackPanel, which has no automation peer, so the click goes to its label.
        By back = By.XPath("//*[@Name='Back']");
        ClickUntil(s, back, By.XPath("//*[@Name='Configure Automatic Upgrades']"));
        ClickUntil(s, back, ById("ForceUpdate"));
        Assert.Equal("Update scheduled for maintenance window", TextById(s, "UpdateTimeLeft"));
        Assert.Equal("Update Now", TextById(s, "ForceUpdate"));
        await VerifyStep(Capture(s), name, "03-main-menu-update-scheduled");
    }

    private static async Task SaveBadUpdateUrls(string name, ReleaseStreamServer releaseStream)
    {
        await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
        WaitForText(s, "UpdateUrl", releaseStream.Url, SettingsWriteTimeout);

        // UIA reports the monitor's \n line breaks as \r\n.
        SaveRejectedUrl(s, releaseStream, "not-a-url", "The url supplied is invalid: \r\nnot-a-url\r\n");
        await VerifyStep(Capture(s), name, "01-not-a-url-rejected");
        CloseError(s, releaseStream);

        string refused = SaveRejectedUrl(s, releaseStream, UnreachableUrl, null);
        Assert.StartsWith("Unable to connect to the remote server:", refused);
        await VerifyStep(Capture(s), name, "02-unreachable-url-rejected");
        CloseError(s, releaseStream);

        // Only typed into the box, never saved, since saving it would fetch the production stream.
        ClickAt(s, WaitForId(s, "ResetUrlButton"));
        WaitForText(s, "UpdateUrl", "https://get.openziti.io/zdew/stable.json", AppResponseTimeout);
        await VerifyStep(Capture(s), name, "03-reset-to-default-url");
    }

    /// <summary>
    /// Type url, click Save Settings, and return the error dialog's details once it shows. expectedDetails, when set,
    /// must match them exactly. The monitor must have left the saved URL alone.
    /// </summary>
    private static string SaveRejectedUrl(AppiumSession s, ReleaseStreamServer releaseStream, string url,
        string? expectedDetails)
    {
        TypeUntil(s, ById("UpdateUrl"), url);
        ClickAt(s, WaitForId(s, "SaveSettingsButton"));
        // The monitor checks the URL by fetching it before it answers, so this waits as long as an update check.
        WaitFor(s, By.XPath("//*[@AutomationId='ErrorTitle' and @Name='Error Saving Settings']"), UpdateCheckTimeout);
        string details = TextById(s, "ErrorDetails");
        if (expectedDetails != null)
            Assert.Equal(expectedDetails, details);
        Assert.Equal(releaseStream.Url, (string?)SavedSettings()?["AutomaticUpdateURL"]);
        return details;
    }

    /// <summary>Close the error dialog, after which the box shows the saved URL again.</summary>
    private static void CloseError(AppiumSession s, ReleaseStreamServer releaseStream)
    {
        ClickUntilGone(s, By.XPath("//Button[@Name='Close Error']"));
        WaitForText(s, "UpdateUrl", releaseStream.Url, AppResponseTimeout);
    }

    private static async Task ShowPolicyLock(string name, ReleaseStreamServer releaseStream)
    {
        await using AppiumSession s = await LaunchOnAutomaticUpgrades(name);
        WaitFor(s, PolicyBanner);
        WaitForText(s, "UpdateUrl", releaseStream.Url, SettingsWriteTimeout);
        Assert.Equal("02:00", TextById(s, "MaintenanceWindowStartCombo"));
        Assert.Equal("05:00", TextById(s, "MaintenanceWindowEndCombo"));
        Assert.False(WaitForId(s, "AutomaticUpgradesToggle").Enabled);
        Assert.False(WaitForId(s, "UpdateUrl").Enabled);
        Assert.False(WaitForId(s, "MaintenanceWindowStartCombo").Enabled);
        Assert.Null(FindByAccessibilityId(s, "ResetUrlButton"));
        Assert.Null(FindByAccessibilityId(s, "SaveSettingsButton"));
        await VerifyStep(Capture(s), name, "01-locked-by-policy");

        Registry.LocalMachine.DeleteSubKey(PolicyKeyPath);
        WaitForText(s, "UpdateUrl", UnreachableUrl, PolicyReloadTimeout);
        WaitForGone(s, PolicyBanner);
        Assert.Equal("01:00", TextById(s, "MaintenanceWindowStartCombo"));
        Assert.Equal("04:00", TextById(s, "MaintenanceWindowEndCombo"));
        Assert.True(WaitForId(s, "AutomaticUpgradesToggle").Enabled);
        Assert.True(WaitForId(s, "UpdateUrl").Enabled);
        Assert.True(WaitForId(s, "MaintenanceWindowStartCombo").Enabled);
        WaitForId(s, "SaveSettingsButton");
        await VerifyStep(Capture(s), name, "02-policy-removed");
    }

    private static async Task<AppiumSession> LaunchOnMainMenu(string name)
    {
        AppiumSession s = await AppiumSession.LaunchAgainstMonitorAsync(DefaultExePath(),
            DefaultMonitorPath(), Fixture("landing-status.json"), UiLogPath(name), MonitorLogPath(name));
        return await PrepareOrDispose(s, session =>
        {
            WaitForId(session, "ConnectLabel");
            OpenMainMenu(session);
        });
    }

    private static async Task<AppiumSession> LaunchOnAutomaticUpgrades(string name)
    {
        AppiumSession s = await LaunchOnMainMenu(name);
        return await PrepareOrDispose(s, session =>
        {
            ClickUntil(session, By.XPath("//*[@Name='Advanced Settings']"),
                By.XPath("//*[@Name='Configure Automatic Upgrades']"));
            ClickUntil(session, By.XPath("//*[@Name='Configure Automatic Upgrades']"), ById("AutomaticUpgradesToggle"));
        });
    }

    /// <summary>Open the combo and click its item named item.</summary>
    private static void SelectComboItem(AppiumSession s, string comboId, string item)
    {
        // Scoped to the combo, so another combo's item with the same name never matches, and the search stays fast.
        IWebElement combo = WaitForId(s, comboId);
        By onscreenItem = By.XPath(".//ListItem[@IsOffscreen='False']");
        By row = By.XPath($".//ListItem[@Name='{item}']");
        IWebElement? shown = null;
        ClickUntil(s, ById(comboId), $"{comboId} lists {item}", () =>
        {
            shown = combo.FindElements(row).FirstOrDefault(e => e.Displayed);
            return shown != null;
        });
        ClickAt(s, shown!);
        WaitUntil(s, $"{comboId} closes", TimeSpan.FromSeconds(5),
            () => combo.FindElements(onscreenItem).Count == 0);
        WaitForText(s, comboId, item, SettingsWriteTimeout);
    }

    private static void WaitForText(AppiumSession s, string id, string text, TimeSpan timeout) =>
        WaitUntil(s, $"{id} shows {text}", timeout, () => TextById(s, id) == text);

    /// <summary>The automatic upgrades label once it is shown and no longer reads previous.</summary>
    private static string WaitForLabelChange(AppiumSession s, string previous)
    {
        string label = "";
        WaitUntil(s, $"the automatic upgrades label changes from {previous}", SettingsWriteTimeout, () =>
        {
            label = TextById(s, "AutomaticUpgradesToggleLabel");
            return label != "" && label != previous;
        });
        return label;
    }

    /// <summary>The saved AutomaticUpdatesDisabled once the monitor has written a value other than previous.</summary>
    private static bool WaitForSavedChange(AppiumSession s, bool? previous)
    {
        bool? saved = null;
        WaitUntil(s, $"the monitor saves AutomaticUpdatesDisabled over {previous?.ToString() ?? "no settings file"}",
            SettingsWriteTimeout, () =>
            {
                saved = (bool?)SavedSettings()?["AutomaticUpdatesDisabled"];
                return saved != null && saved != previous;
            });
        return saved!.Value;
    }

    /// <summary>The saved maintenance window once the monitor has written one other than previous.</summary>
    private static MaintenanceWindow WaitForSavedWindowChange(AppiumSession s, MaintenanceWindow previous)
    {
        MaintenanceWindow? saved = null;
        WaitUntil(s, $"the monitor saves a maintenance window other than {previous}", SettingsWriteTimeout, () =>
        {
            JObject? settings = SavedSettings();
            saved = settings == null ? null : MaintenanceWindow.From(settings);
            return saved != null && saved != previous;
        });
        return saved!;
    }

    /// <summary>
    /// Run test with the monitor's settings.json holding start, or with no file when start is null, then put back what
    /// was there. The put back runs after test disposes its monitor, so nothing overwrites it.
    /// </summary>
    private static async Task WithSettings(byte[]? start, Func<Task> test)
    {
        string path = MonitorProcess.SettingsPath();
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
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

    /// <summary>Where a Windows upgrade backs up liveFolder, the same path the monitor's restore reads.</summary>
    private static string WindowsOldFolder(string liveFolder)
    {
        string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        return Path.Combine(systemDrive, "Windows.old", liveFolder.Substring(Path.GetPathRoot(liveFolder)!.Length));
    }

    /// <summary>
    /// Run test with backupFolder holding a settings.json of content, then remove the file and every folder this
    /// created. An existing backupFolder may be a real upgrade's backup, so it throws instead of touching it.
    /// </summary>
    private static async Task WithBackup(string backupFolder, byte[] content, Func<Task> test)
    {
        if (Directory.Exists(backupFolder))
            throw new InvalidOperationException($"{backupFolder} already exists, so it may hold a real upgrade backup");
        List<string> created = new List<string>();
        for (string? folder = backupFolder; folder != null && !Directory.Exists(folder);
             folder = Path.GetDirectoryName(folder))
            created.Add(folder);
        string backupFile = Path.Combine(backupFolder, "settings.json");
        Directory.CreateDirectory(backupFolder);
        File.WriteAllBytes(backupFile, content);
        try
        {
            await test();
        }
        finally
        {
            if (File.Exists(backupFile))
                File.Delete(backupFile);
            // Deepest first, and the monitor removes backupFolder itself once it has restored the file.
            foreach (string folder in created.Where(Directory.Exists))
                Directory.Delete(folder);
        }
    }

    /// <summary>
    /// Run test with the monitor's policy key holding values, then delete every key this created. An existing policy key
    /// may be the machine's real policy, so it throws instead of touching it. Writing under HKLM needs an elevated run.
    /// </summary>
    private static async Task WithPolicy(IReadOnlyDictionary<string, object> values, Func<Task> test)
    {
        if (KeyExists(PolicyKeyPath))
            throw new InvalidOperationException($@"HKLM\{PolicyKeyPath} already exists, so it may be a real policy");
        string topCreated = PolicyKeyPath;
        while (!KeyExists(topCreated[..topCreated.LastIndexOf('\\')]))
            topCreated = topCreated[..topCreated.LastIndexOf('\\')];
        using (RegistryKey key = Registry.LocalMachine.CreateSubKey(PolicyKeyPath))
        {
            foreach ((string name, object value) in values)
                key.SetValue(name, value);
        }
        try
        {
            await test();
        }
        finally
        {
            // The test may have deleted the policy key itself.
            Registry.LocalMachine.DeleteSubKeyTree(topCreated, throwOnMissingSubKey: false);
        }
    }

    private static bool KeyExists(string path)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
        return key != null;
    }

    /// <summary>Make the monitor's settings.json hold content, or delete it when content is null.</summary>
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

    /// <summary>The monitor's settings.json, or null while the file is missing or mid-write.</summary>
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
