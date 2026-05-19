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

namespace ZitiDesktopEdge.ViewModels {
    public class GetStartedViewModel : INotifyPropertyChanged {
        private bool _isOpen;
        private bool _userDismissed;

        public bool IsOpen {
            get => _isOpen;
            private set {
                if (_isOpen == value) return;
                _isOpen = value;
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(Visibility));
            }
        }

        public Visibility Visibility => _isOpen ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// Show when there are zero identities, unless the user has explicitly dismissed it
        /// for this session. Reset the dismissal once identities exist again.
        /// </summary>
        public void UpdateForIdentityCount(int identityCount) {
            if (identityCount > 0) {
                IsOpen = false;
                _userDismissed = false;
                return;
            }
            if (_userDismissed) return;
            IsOpen = true;
        }

        public void Close() {
            _userDismissed = true;
            IsOpen = false;
        }

        /// <summary>
        /// Force the welcome screen open, bypassing the session dismissal. Called when the user
        /// explicitly asks for it back (footer button or tray menu).
        /// </summary>
        public void Show() {
            _userDismissed = false;
            IsOpen = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
