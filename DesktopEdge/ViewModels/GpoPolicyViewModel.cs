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

namespace ZitiDesktopEdge.ViewModels {
    /// <summary>
    /// ViewModel exposing Windows Group Policy lock state for XAML data binding.
    /// Wraps a <see cref="GpoPolicyState"/> model and provides both lock indicators
    /// (for "managed by your organization" labels) and inverted editability flags
    /// (for binding to IsEnabled without a converter).
    /// </summary>
    public sealed class GpoPolicyViewModel : INotifyPropertyChanged {
        private GpoPolicyState _state = new GpoPolicyState();

        public event PropertyChangedEventHandler PropertyChanged;

        public void ApplyState(GpoPolicyState state) {
            _state = state;
            // empty string refreshes all bindings on this object
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        // ---- GPO lock indicators (bind to Visibility for "managed by policy" labels) ----

        public bool IsLogLevelGpoLocked => _state.LogLevelLocked;
        public bool IsTunSettingsGpoLocked => _state.TunSettingsLocked;
        public bool IsAutoUpdatesGpoLocked => _state.AutomaticUpdatesDisabledLocked;
        public bool IsUpdateUrlGpoLocked => _state.UpdateStreamUrlLocked;
        public bool IsDefaultExtAuthGpoLocked => _state.DefaultExtAuthProviderLocked;

        // ---- Inverted editability (bind to IsEnabled on controls) ----

        public bool IsLogLevelEditable => !_state.LogLevelLocked;
        public bool IsTunSettingsEditable => !_state.TunSettingsLocked;
        public bool IsAutoUpdatesEditable => !_state.AutomaticUpdatesDisabledLocked;
        public bool IsUpdateUrlEditable => !_state.UpdateStreamUrlLocked;
        public bool IsDefaultExtAuthEditable => !_state.DefaultExtAuthProviderLocked;

        // ---- GPO values (for display when locked) ----

        public bool GpoAutomaticUpdatesDisabled => _state.GpoAutomaticUpdatesDisabled;
        public string GpoUpdateStreamUrl => _state.GpoUpdateStreamUrl;
        public string GpoDefaultExtAuthProvider => _state.DefaultExtAuthProvider;
    }
}
