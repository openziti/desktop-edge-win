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

using System.ComponentModel;
using Ziti.Desktop.Edge.Models;
using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge.ViewModels {
    /// <summary>
    /// ViewModel exposing organizational policy lock state for XAML data binding.
    /// Wraps a <see cref="ManagedSettingsState"/> model and provides both lock indicators
    /// (for "managed by your organization" labels) and inverted editability flags
    /// (for binding to IsEnabled without a converter).
    ///
    /// The policy source may be Group Policy, Intune, MDM, or any tool that writes
    /// to HKLM\SOFTWARE\Policies\NetFoundry\...
    /// </summary>
    public sealed class ManagedSettingsViewModel : INotifyPropertyChanged {
        private ManagedSettingsState _state = new ManagedSettingsState();

        public event PropertyChangedEventHandler PropertyChanged;

        public void ApplyState(ManagedSettingsState state) {
            _state = state;
            // empty string refreshes all bindings on this object
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        /// <summary>
        /// Updates the monitor-service policy lock state from a live IPC status event.
        /// Called each time the service sends a MonitorServiceStatusEvent so the UI
        /// reflects the current registry policy without requiring a restart.
        /// </summary>
        public void ApplyFromEvent(MonitorServiceStatusEvent evt) {
            _state.AutomaticUpdatesDisabledLocked = evt.AutomaticUpgradeDisabledLocked;
            _state.PolicyAutomaticUpdatesDisabled = evt.AutomaticUpgradeDisabledLocked && bool.TryParse(evt.AutomaticUpgradeDisabled, out bool v) && v;
            _state.UpdateStreamUrlLocked          = evt.AutomaticUpgradeURLLocked;
            _state.PolicyUpdateStreamUrl          = evt.AutomaticUpgradeURLLocked ? evt.AutomaticUpgradeURL : null;
            AutomaticUpgradesPolicyControlled     = evt.AutomaticUpgradeDisabledLocked
                                                  || evt.AutomaticUpgradeURLLocked
                                                  || evt.UpdateIntervalLocked
                                                  || evt.InstallationReminderLocked
                                                  || evt.InstallationCriticalLocked
                                                  || evt.AlivenessChecksBeforeActionLocked
                                                  || evt.MaintenanceWindowStartLocked
                                                  || evt.MaintenanceWindowEndLocked;
            AutomaticUpdatesDisabled              = bool.TryParse(evt.AutomaticUpgradeDisabled, out bool d) && d;
            AutomaticUpdateURL                    = evt.AutomaticUpgradeURL;
            MaintenanceWindowStart                = evt.MaintenanceWindowStart;
            MaintenanceWindowEnd                  = evt.MaintenanceWindowEnd;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        // ---- Monitor service connection state ----

        /// <summary>
        /// True while the UI has an active IPC connection to the ziti-monitor service.
        /// The Automatic Upgrades page shows a "service not running" message when false
        /// instead of displaying stale cached values from the last connection.
        /// </summary>
        private bool _isMonitorConnected = false;
        public bool IsMonitorConnected {
            get => _isMonitorConnected;
            set {
                _isMonitorConnected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMonitorConnected)));
            }
        }

        // ---- Single policy-controlled flag for the Automatic Upgrades screen ----

        /// <summary>
        /// True when any automatic-upgrade setting is controlled by organizational policy.
        /// Drives the banner visibility and control editability for the whole screen.
        /// </summary>
        public bool AutomaticUpgradesPolicyControlled { get; private set; }

        // ---- All Automatic Upgrades screen values (updated alongside lock state in ApplyFromEvent) ----

        public bool   AutomaticUpdatesDisabled { get; private set; }
        public string AutomaticUpdateURL       { get; private set; }
        public int?   MaintenanceWindowStart   { get; private set; }
        public int?   MaintenanceWindowEnd     { get; private set; }

        // ---- Policy lock indicators (bind to Visibility for "managed by your organization" labels) ----

        public bool IsAutoUpdatesPolicyLocked => _state.AutomaticUpdatesDisabledLocked;
        public bool IsUpdateUrlPolicyLocked => _state.UpdateStreamUrlLocked;
        public bool IsDefaultExtAuthPolicyLocked => _state.DefaultExtAuthProviderLocked;

        // ---- Inverted editability (bind to IsEnabled on controls) ----

        public bool IsAutoUpdatesEditable => !_state.AutomaticUpdatesDisabledLocked;
        public bool IsUpdateUrlEditable => !_state.UpdateStreamUrlLocked;
        public bool IsDefaultExtAuthEditable => !_state.DefaultExtAuthProviderLocked;

        // ---- Policy values (for display when locked) ----

        public bool PolicyAutomaticUpdatesDisabled => _state.PolicyAutomaticUpdatesDisabled;
        public string PolicyUpdateStreamUrl => _state.PolicyUpdateStreamUrl;
        public string PolicyDefaultExtAuthProvider => _state.DefaultExtAuthProvider;
    }
}
