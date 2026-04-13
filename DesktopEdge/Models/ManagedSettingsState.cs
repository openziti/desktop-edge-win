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

namespace Ziti.Desktop.Edge.Models {
    /// <summary>
    /// Snapshot of which settings are currently controlled by organizational policy
    /// (registry values under HKLM\SOFTWARE\Policies\NetFoundry\...).
    /// The policy source may be Group Policy, Intune, MDM, or any tool that writes
    /// to that registry path. Populated by ManagedSettingsReader, consumed by
    /// ManagedSettingsViewModel.
    /// </summary>
    public sealed class ManagedSettingsState {
        // ziti-monitor-service locks
        public bool AutomaticUpdatesDisabledLocked { get; set; }
        public bool PolicyAutomaticUpdatesDisabled { get; set; }
        public bool UpdateStreamUrlLocked { get; set; }
        public string PolicyUpdateStreamUrl { get; set; }

        // UI-only policy values
        public string DefaultExtAuthProvider { get; set; }

        public bool DefaultExtAuthProviderLocked =>
            DefaultExtAuthProvider != null;
    }
}
