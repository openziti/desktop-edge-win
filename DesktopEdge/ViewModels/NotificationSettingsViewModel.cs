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
using ZitiDesktopEdge.Properties;

namespace ZitiDesktopEdge.ViewModels {
    /// <summary>
    /// Which categories of OS toast the app is allowed to raise. Only AuthNotificationsEnabled is
    /// persisted so far, through the existing user-scoped setting. The other three live for the
    /// lifetime of the process: they need settings of their own, which cannot be added honestly
    /// until openziti/desktop-edge-win#1054 stops user settings resetting on upgrade.
    /// </summary>
    public sealed class NotificationSettingsViewModel : INotifyPropertyChanged {

        private bool _authResultNotificationsEnabled = true;
        // Off by default, matching the ShowUnexpectedFailure appSetting this toggle replaced.
        private bool _connectionNotificationsEnabled = false;
        private bool _updateNotificationsEnabled = true;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Identities asking for MFA or external authentication.</summary>
        public bool AuthNotificationsEnabled {
            get => Settings.Default.AuthNotificationsEnabled;
            set {
                Settings.Default.AuthNotificationsEnabled = value;
                Settings.Default.Save();
                OnPropertyChanged(nameof(AuthNotificationsEnabled));
            }
        }

        /// <summary>The outcome of an authentication attempt, successful or failed.</summary>
        public bool AuthResultNotificationsEnabled {
            get => _authResultNotificationsEnabled;
            set {
                _authResultNotificationsEnabled = value;
                OnPropertyChanged(nameof(AuthResultNotificationsEnabled));
            }
        }

        /// <summary>The data channel to the tunneler dropping unexpectedly.</summary>
        public bool ConnectionNotificationsEnabled {
            get => _connectionNotificationsEnabled;
            set {
                _connectionNotificationsEnabled = value;
                OnPropertyChanged(nameof(ConnectionNotificationsEnabled));
            }
        }

        /// <summary>A new version being available or about to install.</summary>
        public bool UpdateNotificationsEnabled {
            get => _updateNotificationsEnabled;
            set {
                _updateNotificationsEnabled = value;
                OnPropertyChanged(nameof(UpdateNotificationsEnabled));
            }
        }

        private void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
