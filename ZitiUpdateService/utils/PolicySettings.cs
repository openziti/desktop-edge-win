/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using System;
using System.Configuration;
using System.Management;
using Microsoft.Win32;
using NLog;
using ZitiDesktopEdge.DataStructures;

namespace ZitiUpdateService.Utils {
    /// <summary>
    /// Reads managed-policy overrides from the registry and exposes "effective value" helpers
    /// used throughout UpdateService.  A null/absent registry value means "not policy-controlled"
    /// if unset, the caller falls back to settings.json or App.config.
    ///
    /// Registry root:
    ///   HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
    ///
    /// This key is the standard Windows managed-policy path.  Values are written by Group Policy
    /// Objects (via the supplied ADMX/ADML templates), Microsoft Intune (ADMX ingestion or
    /// OMA-URI), MECM Compliance Settings, or direct registry writes for testing.
    ///
    /// Call <see cref="Load"/> once at startup, then <see cref="StartWatching"/> to receive
    /// <see cref="OnConfigurationChange"/> events whenever the policy key is modified.
    /// </summary>
    internal static class PolicySettings {
        private const string RegistryPath =
            @"SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service";

        // WMI uses HKEY_LOCAL_MACHINE and double-backslashes.
        // We watch the NetFoundry parent tree so the watcher works even when the
        // ziti-monitor-service key doesn't exist yet (RegistryTreeChangeEvent fires
        // for any create/modify/delete anywhere under RootPath).
        private const string WmiHive    = "HKEY_LOCAL_MACHINE";
        private const string WmiRootPath = @"SOFTWARE\\Policies\\NetFoundry";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static ManagementEventWatcher _watcher;
        private static System.Threading.Timer _debounceTimer;

        /// <summary>Fired after <see cref="Load"/> completes in response to a registry change.</summary>
        internal static event EventHandler<ControllerEvent> OnConfigurationChange;

        // Null means not set by policy; fall through to settings.json / App.config
        private static bool?   _disableAutomaticUpdates;
        private static string  _updateStreamURL;
        private static int?    _updateIntervalSeconds;
        private static int?    _installationReminderSeconds;
        private static int?    _installationCriticalSeconds;
        private static int?    _alivenessChecksBeforeAction;
        private static bool?   _deferInstallToRestart;
        private static int?    _maintenanceWindowStart;
        private static int?    _maintenanceWindowEnd;

        /// <summary>
        /// Reads all policy values from the registry.  Called once at startup and again
        /// automatically by the WMI watcher whenever the policy key changes.
        /// </summary>
        internal static void Load() {
            // Reset all fields before reading so removed values don't linger
            _disableAutomaticUpdates     = null;
            _updateStreamURL             = null;
            _updateIntervalSeconds       = null;
            _installationReminderSeconds = null;
            _installationCriticalSeconds = null;
            _alivenessChecksBeforeAction = null;
            _deferInstallToRestart       = null;
            _maintenanceWindowStart      = null;
            _maintenanceWindowEnd        = null;

            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryPath)) {
                    if (key == null) {
                        Logger.Debug("Policy registry key absent, no policy overrides in effect");
                        // Stop the watcher so RetryWatchIfNeeded restarts it next tick and
                        // re-checks whether policy has been written since we last looked.
                        // This handles the case where the watched key was deleted and
                        // recreated — WMI does not fire for creation of a previously-absent key.
                        StopWatching();
                        return;
                    }

                    ReadDword(key,  "AutomaticUpdatesDisabled",     v => _disableAutomaticUpdates     = v != 0);
                    ReadString(key, "AutomaticUpdateURL",             v => _updateStreamURL             = v);
                    ReadDword(key,  "UpdateTimer",                    v => _updateIntervalSeconds       = Math.Max(600, v));
                    ReadDword(key,  "InstallationReminder",           v => _installationReminderSeconds = v);
                    ReadDword(key,  "InstallationCritical",           v => _installationCriticalSeconds = Math.Max(0, v));
                    ReadDword(key,  "AlivenessChecksBeforeAction",    v => _alivenessChecksBeforeAction = Math.Max(1, v));
                    ReadDword(key,  "DeferInstallToRestart",          v => _deferInstallToRestart       = v != 0);
                    ReadDword(key,  "MaintenanceWindowStart",         v => _maintenanceWindowStart      = Math.Min(23, Math.Max(0, v)));
                    ReadDword(key,  "MaintenanceWindowEnd",           v => _maintenanceWindowEnd        = Math.Min(23, Math.Max(0, v)));

                    Logger.Info("Policy overrides loaded: " +
                                "AutomaticUpdatesDisabled={0}, AutomaticUpdateURL={1}, UpdateTimer={2}, " +
                                "InstallationReminder={3}, InstallationCritical={4}, AlivenessChecksBeforeAction={5}, " +
                                "DeferInstallToRestart={6}, MaintenanceWindowStart={7}, MaintenanceWindowEnd={8}",
                        _disableAutomaticUpdates?.ToString()         ?? "(not set)",
                        _updateStreamURL                             ?? "(not set)",
                        _updateIntervalSeconds?.ToString()           ?? "(not set)",
                        _installationReminderSeconds?.ToString()     ?? "(not set)",
                        _installationCriticalSeconds?.ToString()     ?? "(not set)",
                        _alivenessChecksBeforeAction?.ToString()     ?? "(not set)",
                        _deferInstallToRestart?.ToString()           ?? "(not set)",
                        _maintenanceWindowStart?.ToString()          ?? "(not set)",
                        _maintenanceWindowEnd?.ToString()            ?? "(not set)");
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error reading policy registry settings");
            }
        }

        /// <summary>
        /// Returns true if the named setting is currently controlled by managed policy,
        /// meaning runtime changes to that setting must be rejected.
        /// </summary>
        internal static bool IsLocked(string valueName) {
            switch (valueName) {
                case "AutomaticUpdatesDisabled":    return _disableAutomaticUpdates.HasValue;
                case "AutomaticUpdateURL":            return _updateStreamURL != null;
                case "UpdateTimer":      return _updateIntervalSeconds.HasValue;
                case "InstallationReminder":return _installationReminderSeconds.HasValue;
                case "InstallationCritical":return _installationCriticalSeconds.HasValue;
                case "AlivenessChecksBeforeAction": return _alivenessChecksBeforeAction.HasValue;
                case "DeferInstallToRestart":       return _deferInstallToRestart.HasValue;
                case "MaintenanceWindowStart":      return _maintenanceWindowStart.HasValue;
                case "MaintenanceWindowEnd":        return _maintenanceWindowEnd.HasValue;
                default:                            return false;
            }
        }

        /// <summary>
        /// True if at least one policy value was present in the registry when <see cref="Load"/>
        /// last ran.  Used at startup to detect the "GP not yet applied" boot race and delay
        /// the first update check accordingly.
        /// </summary>
        internal static bool HasPolicy =>
            _disableAutomaticUpdates.HasValue ||
            _updateStreamURL         != null  ||
            _updateIntervalSeconds.HasValue   ||
            _installationReminderSeconds.HasValue ||
            _installationCriticalSeconds.HasValue ||
            _alivenessChecksBeforeAction.HasValue ||
            _deferInstallToRestart.HasValue   ||
            _maintenanceWindowStart.HasValue  ||
            _maintenanceWindowEnd.HasValue;

        // ---- Effective-value helpers -------------------------------------------------
        // Each returns the policy value if set, otherwise falls back to the supplied
        // settings.json value or the App.config default.

        internal static bool EffectiveAutomaticUpdatesDisabled(Settings s) =>
            _disableAutomaticUpdates ?? s.AutomaticUpdatesDisabled;

        internal static string EffectiveAutomaticUpdateURL(Settings s) =>
            _updateStreamURL ?? s.AutomaticUpdateURL;

        internal static int EffectiveAlivenessChecksBeforeAction(Settings s) =>
            _alivenessChecksBeforeAction ?? s.AlivenessChecksBeforeAction ?? 12;

        internal static TimeSpan EffectiveUpdateInterval() {
            if (_updateIntervalSeconds.HasValue) {
                return TimeSpan.FromSeconds(_updateIntervalSeconds.Value);
            }
            TimeSpan t;
            return TimeSpan.TryParse(ConfigurationManager.AppSettings.Get("UpdateTimer"), out t)
                ? t
                : TimeSpan.FromMinutes(10);
        }

        internal static TimeSpan EffectiveInstallationReminder() {
            if (_installationReminderSeconds.HasValue) {
                return TimeSpan.FromSeconds(_installationReminderSeconds.Value);
            }
            TimeSpan t;
            return TimeSpan.TryParse(ConfigurationManager.AppSettings.Get("InstallationReminder"), out t)
                ? t
                : TimeSpan.FromDays(1);
        }

        internal static bool EffectiveDeferInstallToRestart(Settings s) =>
            _deferInstallToRestart ?? s.DeferInstallToRestart;

        internal static int? EffectiveMaintenanceWindowStart(Settings s) =>
            _maintenanceWindowStart ?? s.MaintenanceWindowStart;

        internal static int? EffectiveMaintenanceWindowEnd(Settings s) =>
            _maintenanceWindowEnd ?? s.MaintenanceWindowEnd;

        internal static TimeSpan EffectiveInstallationCritical() {
            if (_installationCriticalSeconds.HasValue) {
                return TimeSpan.FromSeconds(_installationCriticalSeconds.Value);
            }
            TimeSpan t;
            return TimeSpan.TryParse(ConfigurationManager.AppSettings.Get("InstallationCritical"), out t)
                ? t
                : TimeSpan.FromDays(7);
        }

        // ---- Registry watcher -------------------------------------------------------

        /// <summary>
        /// Starts a WMI <c>RegistryKeyChangeEvent</c> watcher on the managed-policy key.  Any
        /// change (value added, modified, or deleted) triggers a <see cref="Load"/> followed by
        /// <see cref="OnConfigurationChange"/>.  Safe to call more than once — a running
        /// watcher is stopped and replaced.
        /// </summary>
        /// <summary>
        /// Retries <see cref="StartWatching"/> only if the watcher is not already running.
        /// Call periodically (e.g. on each update-check timer tick) so that transient WMI
        /// startup failures self-heal without requiring a service restart.
        /// </summary>
        internal static void RetryWatchIfNeeded() {
            if (_watcher != null) return;
            Logger.Debug("Policy registry watcher not running, retrying...");
            StartWatching();
            // Reload policy now because changes written while the watcher was down
            // will not re-fire and would otherwise be silently missed.
            if (_watcher != null) {
                Logger.Debug("Policy registry watcher (re)started, reloading policy in case values changed while watcher was down");
                Load();
                if (HasPolicy) {
                    OnConfigurationChange?.Invoke(null, null);
                }
            }
        }

        internal static void StartWatching() {
            StopWatching();
            try {
                // RegistryTreeChangeEvent fires for any create/modify/delete under RootPath,
                // including when the ziti-monitor-service key itself is created or removed.
                // This means the watcher works regardless of whether the key exists at startup.
                var query = new WqlEventQuery(
                    "SELECT * FROM RegistryTreeChangeEvent WHERE " +
                    $"Hive='{WmiHive}' AND RootPath='{WmiRootPath}'");

                var scope = new ManagementScope(@"root\default");
                _watcher = new ManagementEventWatcher(scope, query);
                _watcher.EventArrived += OnRegistryChanged;
                _watcher.Start();
                _debounceTimer = new System.Threading.Timer(OnDebounceElapsed, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                Logger.Debug("Policy registry watcher started (watching tree: {0}\\{1})", WmiHive, WmiRootPath);
            } catch (Exception) {
                Logger.Debug("Policy registry watcher unavailable, local settings active. Policy-related changes will require a service restart to take effect.");
            }
        }

        /// <summary>Stops the WMI registry watcher if it is running.</summary>
        internal static void StopWatching() {
            if (_watcher == null) return;
            try {
                _watcher.Stop();
                _watcher.Dispose();
                _debounceTimer?.Dispose();
                Logger.Debug("Policy registry watcher stopped");
            } catch (Exception ex) {
                Logger.Warn(ex, "Error stopping policy registry watcher");
            } finally {
                _watcher = null;
                _debounceTimer = null;
            }
        }

        private static void OnRegistryChanged(object sender, EventArrivedEventArgs e) {
            // Trailing-edge debounce: reset the timer on every event so Load() fires
            // only after registry operations have stopped for 500ms.  This ensures we
            // read the final settled state rather than a partially-written intermediate.
            Logger.Debug("Policy registry event received, restarting debounce timer");
            _debounceTimer?.Change(500, System.Threading.Timeout.Infinite);
        }

        private static void OnDebounceElapsed(object state) {
            Logger.Info("Policy registry key changed, reloading policy overrides");

            var before = SnapshotFields();
            Load();
            var after = SnapshotFields();

            LogFieldChanges(before, after);
            OnConfigurationChange?.Invoke(null, null);
        }

        private static (bool? disable, string url, int? interval, int? reminder, int? critical, int? aliveness, bool? defer, int? winStart, int? winEnd) SnapshotFields() =>
            (_disableAutomaticUpdates, _updateStreamURL, _updateIntervalSeconds,
             _installationReminderSeconds, _installationCriticalSeconds, _alivenessChecksBeforeAction,
             _deferInstallToRestart, _maintenanceWindowStart, _maintenanceWindowEnd);

        private static void LogFieldChanges(
            (bool? disable, string url, int? interval, int? reminder, int? critical, int? aliveness, bool? defer, int? winStart, int? winEnd) before,
            (bool? disable, string url, int? interval, int? reminder, int? critical, int? aliveness, bool? defer, int? winStart, int? winEnd) after) {

            void Log(string name, object oldVal, object newVal) {
                string o = oldVal?.ToString() ?? "(not set)";
                string n = newVal?.ToString() ?? "(not set)";
                if (o != n) Logger.Info("Policy {0} {1} -> {2}", name, o, n);
                else        Logger.Debug("Policy {0} {1} (unchanged)", name, o);
            }

            Log("AutomaticUpdatesDisabled",    before.disable,   after.disable);
            Log("AutomaticUpdateURL",          before.url,       after.url);
            Log("UpdateTimer",                 before.interval,  after.interval);
            Log("InstallationReminder",        before.reminder,  after.reminder);
            Log("InstallationCritical",        before.critical,  after.critical);
            Log("AlivenessChecksBeforeAction", before.aliveness, after.aliveness);
            Log("DeferInstallToRestart",       before.defer,     after.defer);
            Log("MaintenanceWindowStart",      before.winStart,  after.winStart);
            Log("MaintenanceWindowEnd",        before.winEnd,    after.winEnd);
        }

        // ---- Private helpers --------------------------------------------------------

        private static void ReadDword(RegistryKey key, string name, Action<int> setter) {
            object v = key.GetValue(name);
            if (v is int i) {
                setter(i);
                Logger.Debug("Policy registry: {0} = {1}", name, i);
            }
        }

        private static void ReadString(RegistryKey key, string name, Action<string> setter) {
            string v = key.GetValue(name) as string;
            if (!string.IsNullOrWhiteSpace(v)) {
                setter(v);
                Logger.Debug("Policy registry: {0} = {1}", name, v);
            }
        }
    }
}
