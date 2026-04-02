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
using Microsoft.Win32;
using NLog;

namespace ZitiUpdateService.Utils {
    /// <summary>
    /// Reads Group Policy overrides from the registry at service startup and exposes
    /// "effective value" helpers used throughout UpdateService.  A null/absent registry
    /// value means "not GPO-controlled" — the caller falls back to settings.json or
    /// App.config.
    ///
    /// Registry root:
    ///   HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
    /// </summary>
    internal static class GpoSettings {
        private const string RegistryPath =
            @"SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Null means not set by GPO — fall through to settings.json / App.config
        private static bool?   _disableAutomaticUpdates;
        private static string  _updateStreamURL;
        private static int?    _updateIntervalSeconds;
        private static int?    _installationReminderSeconds;
        private static int?    _installationCriticalSeconds;
        private static int?    _alivenessChecksBeforeAction;

        /// <summary>
        /// Read all GPO values from the registry.  Call once at service startup,
        /// after settings.json is loaded but before any timer or update logic runs.
        /// </summary>
        internal static void Load() {
            try {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(RegistryPath)) {
                    if (key == null) {
                        Logger.Debug("GPO registry key absent — no policy overrides in effect");
                        return;
                    }

                    ReadDword(key,  "DisableAutomaticUpdates",    v => _disableAutomaticUpdates    = v != 0);
                    ReadString(key, "UpdateStreamURL",             v => _updateStreamURL            = v);
                    ReadDword(key,  "UpdateIntervalSeconds",       v => _updateIntervalSeconds      = Math.Max(600, v));
                    ReadDword(key,  "InstallationReminderSeconds", v => _installationReminderSeconds = v);
                    ReadDword(key,  "InstallationCriticalSeconds", v => _installationCriticalSeconds = Math.Max(0, v));
                    ReadDword(key,  "AlivenessChecksBeforeAction", v => _alivenessChecksBeforeAction = Math.Max(1, v));

                    Logger.Info("GPO overrides loaded — " +
                                "DisableAutomaticUpdates={0}, UpdateStreamURL={1}, UpdateIntervalSeconds={2}, " +
                                "InstallationReminderSeconds={3}, InstallationCriticalSeconds={4}, AlivenessChecksBeforeAction={5}",
                        _disableAutomaticUpdates?.ToString()         ?? "(not set)",
                        _updateStreamURL                             ?? "(not set)",
                        _updateIntervalSeconds?.ToString()           ?? "(not set)",
                        _installationReminderSeconds?.ToString()     ?? "(not set)",
                        _installationCriticalSeconds?.ToString()     ?? "(not set)",
                        _alivenessChecksBeforeAction?.ToString()     ?? "(not set)");
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error reading GPO registry settings");
            }
        }

        /// <summary>
        /// Returns true if the named setting is currently controlled by Group Policy,
        /// meaning runtime changes to that setting must be rejected.
        /// </summary>
        internal static bool IsLocked(string valueName) {
            switch (valueName) {
                case "DisableAutomaticUpdates":    return _disableAutomaticUpdates.HasValue;
                case "UpdateStreamURL":            return _updateStreamURL != null;
                case "UpdateIntervalSeconds":      return _updateIntervalSeconds.HasValue;
                case "InstallationReminderSeconds":return _installationReminderSeconds.HasValue;
                case "InstallationCriticalSeconds":return _installationCriticalSeconds.HasValue;
                case "AlivenessChecksBeforeAction":return _alivenessChecksBeforeAction.HasValue;
                default:                           return false;
            }
        }

        // ---- Effective-value helpers -------------------------------------------------
        // Each returns the GPO value if set, otherwise falls back to the supplied
        // settings.json value or the App.config default.

        internal static bool EffectiveDisableAutomaticUpdates(Settings s) =>
            _disableAutomaticUpdates ?? s.AutomaticUpdatesDisabled;

        internal static string EffectiveUpdateStreamURL(Settings s) =>
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

        internal static TimeSpan EffectiveInstallationCritical() {
            if (_installationCriticalSeconds.HasValue) {
                return TimeSpan.FromSeconds(_installationCriticalSeconds.Value);
            }
            TimeSpan t;
            return TimeSpan.TryParse(ConfigurationManager.AppSettings.Get("InstallationCritical"), out t)
                ? t
                : TimeSpan.FromDays(7);
        }

        // ---- Private helpers --------------------------------------------------------

        private static void ReadDword(RegistryKey key, string name, Action<int> setter) {
            object v = key.GetValue(name);
            if (v is int i) {
                setter(i);
                Logger.Debug("GPO registry: {0} = {1}", name, i);
            }
        }

        private static void ReadString(RegistryKey key, string name, Action<string> setter) {
            string v = key.GetValue(name) as string;
            if (!string.IsNullOrWhiteSpace(v)) {
                setter(v);
                Logger.Debug("GPO registry: {0} = {1}", name, v);
            }
        }
    }
}
