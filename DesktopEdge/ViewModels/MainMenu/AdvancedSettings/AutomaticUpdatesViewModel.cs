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
using System.Windows;

namespace ZitiDesktopEdge {
    public class AutomaticUpdatesViewModel : INotifyPropertyChanged {
        private bool _monitorConnected;
        private bool _enabled;
        private bool _policyControlled;
        private bool _isChecking;
        private string _updateUrl;

        public bool MonitorConnected {
            get { return _monitorConnected; }
            set { _monitorConnected = value; RaiseDerived(); }
        }

        public bool Enabled {
            get { return _enabled; }
            set { _enabled = value; RaiseDerived(); }
        }

        public bool PolicyControlled {
            get { return _policyControlled; }
            set { _policyControlled = value; RaiseDerived(); }
        }

        public string UpdateUrl {
            get { return _updateUrl; }
            set { _updateUrl = value; OnPropertyChanged(nameof(UpdateUrl)); }
        }

        // Transient: true while a manual "check for updates" is in flight, disables the button.
        public bool IsChecking {
            get { return _isChecking; }
            set { _isChecking = value; RaiseDerived(); }
        }

        // Details (URL, check-for-update, maintenance, save) are editable only when upgrades are on
        // and not locked by policy.
        private bool DetailsEditable => _enabled && !_policyControlled;

        public string ToggleLabel => _enabled ? "ENABLED" : "DISABLED";
        public Visibility MonitorOfflineVisibility => _monitorConnected ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContentVisibility => _monitorConnected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PolicyManagedVisibility => _policyControlled ? Visibility.Visible : Visibility.Collapsed;
        public bool UrlEnabled => DetailsEditable;
        public double DetailsOpacity => DetailsEditable ? 1.0 : 0.3;
        public Visibility UrlWarningVisibility => DetailsEditable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ResetButtonVisibility => DetailsEditable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SaveButtonVisibility => DetailsEditable ? Visibility.Visible : Visibility.Collapsed;
        public bool CheckForUpdateEnabled => _enabled && !_isChecking;

        private void RaiseDerived() {
            OnPropertyChanged(nameof(ToggleLabel));
            OnPropertyChanged(nameof(MonitorOfflineVisibility));
            OnPropertyChanged(nameof(ContentVisibility));
            OnPropertyChanged(nameof(PolicyManagedVisibility));
            OnPropertyChanged(nameof(UrlEnabled));
            OnPropertyChanged(nameof(DetailsOpacity));
            OnPropertyChanged(nameof(UrlWarningVisibility));
            OnPropertyChanged(nameof(ResetButtonVisibility));
            OnPropertyChanged(nameof(SaveButtonVisibility));
            OnPropertyChanged(nameof(CheckForUpdateEnabled));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
