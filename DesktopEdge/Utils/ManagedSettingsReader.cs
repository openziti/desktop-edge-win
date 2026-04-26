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
using Microsoft.Win32;
using NLog;
using Ziti.Desktop.Edge.Models;

namespace Ziti.Desktop.Edge.Utils {
    /// <summary>
    /// Reads organizational policy registry keys for ZDEW components and returns a
    /// <see cref="ManagedSettingsState"/> snapshot.
    ///
    /// The policy source may be Group Policy, Intune, MDM, or any tool that writes
    /// to HKLM\SOFTWARE\Policies\NetFoundry\...
    ///
    /// Registry roots:
    ///   HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ziti-monitor-service
    ///   HKLM\SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows\ui
    /// </summary>
    internal static class ManagedSettingsReader {
        private const string BasePath =
            @"SOFTWARE\Policies\NetFoundry\Ziti Desktop Edge for Windows";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal static ManagedSettingsState Read() {
            ManagedSettingsState state = new ManagedSettingsState();
            try {
                ReadMonitorKeys(state);
                ReadUiKeys(state);
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error reading policy registry settings");
            }

            Logger.Info(
                "Policy state: AutoUpdatesDisabled={0}, UpdateStreamURL={1}, DefaultExtAuthProvider={2}",
                FormatLock(state.AutomaticUpdatesDisabledLocked),
                FormatLock(state.UpdateStreamUrlLocked),
                state.DefaultExtAuthProvider ?? "(not set)");

            return state;
        }

        private static void ReadMonitorKeys(ManagedSettingsState state) {
            using (RegistryKey key = OpenSubKey("ziti-monitor-service")) {
                if (key == null) return;
                object disableVal = key.GetValue("AutomaticUpdatesDisabled");
                if (disableVal is int disableInt) {
                    state.AutomaticUpdatesDisabledLocked = true;
                    state.PolicyAutomaticUpdatesDisabled = disableInt != 0;
                }
                string urlVal = key.GetValue("AutomaticUpdateURL") as string;
                if (!string.IsNullOrWhiteSpace(urlVal)) {
                    state.UpdateStreamUrlLocked = true;
                    state.PolicyUpdateStreamUrl = urlVal;
                }
            }
        }

        private static void ReadUiKeys(ManagedSettingsState state) {
            using (RegistryKey key = OpenSubKey("ui")) {
                if (key == null) return;
                string provider = key.GetValue("DefaultExtAuthProvider") as string;
                if (!string.IsNullOrWhiteSpace(provider)) {
                    state.DefaultExtAuthProvider = provider;
                    Logger.Debug("Policy UI: DefaultExtAuthProvider = {0}", provider);
                }
            }
        }

        private static RegistryKey OpenSubKey(string subKey) {
            string fullPath = BasePath + @"\" + subKey;
            RegistryKey key = Registry.LocalMachine.OpenSubKey(fullPath);
            if (key == null) {
                Logger.Debug("Policy registry key absent: {0}", fullPath);
            }
            return key;
        }

        private static string FormatLock(bool locked) {
            return locked ? "LOCKED" : "unlocked";
        }
    }
}
