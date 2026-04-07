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
    /// Snapshot of which settings are currently controlled by Windows Group Policy
    /// (registry values under HKLM\SOFTWARE\Policies\NetFoundry\...).
    /// Populated by GpoPolicyReader, consumed by GpoPolicyViewModel.
    /// </summary>
    public sealed class GpoPolicyState {
        // ziti-edge-tunnel locks
        public bool LogLevelLocked { get; set; }
        public bool TunIpv4Locked { get; set; }
        public bool TunIpv4MaskLocked { get; set; }
        public bool AddDnsLocked { get; set; }

        // ziti-monitor-service locks
        public bool AutomaticUpdatesDisabledLocked { get; set; }
        public bool GpoAutomaticUpdatesDisabled { get; set; }
        public bool UpdateStreamUrlLocked { get; set; }
        public string GpoUpdateStreamUrl { get; set; }

        // UI-only GPO values
        public string DefaultExtAuthProvider { get; set; }

        /// <summary>
        /// ziti-edge-tunnel treats TunIpv4/TunIpv4Mask/AddDns as a group —
        /// if any one is locked by GPO, all three are locked at runtime.
        /// </summary>
        public bool TunSettingsLocked =>
            TunIpv4Locked || TunIpv4MaskLocked || AddDnsLocked;

        public bool DefaultExtAuthProviderLocked =>
            DefaultExtAuthProvider != null;
    }
}
