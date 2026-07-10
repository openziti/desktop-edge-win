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
using System.ComponentModel;
using System.Windows;
using ZitiDesktopEdge;

namespace ZitiDesktopEdge.ViewModels {
    public class GetStartedViewModel : INotifyPropertyChanged {
        private bool _isOpen;
        private bool _userDismissed;

        public event EventHandler Closed;
        public ActionCommand CloseCommand { get; }

        public GetStartedViewModel() {
            CloseCommand = new ActionCommand(CloseFromButton, () => true);
        }

        private void CloseFromButton() {
            Close();
            Closed?.Invoke(this, EventArgs.Empty);
        }

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
        /// Show only when the ziti service is connected AND there are zero identities, unless
        /// the user has explicitly dismissed it for this session. While the service is
        /// disconnected the welcome screen is force-hidden -- the user can't actually add an
        /// identity without the service running, so the "Service Not Started" overlay takes
        /// precedence.
        /// </summary>
        public void UpdateForState(bool serviceConnected, int identityCount) {
            if (!serviceConnected) {
                IsOpen = false;
                return;
            }
            if (identityCount > 0) {
                IsOpen = false;
                _userDismissed = false;
                return;
            }
            if (_userDismissed) return;
            IsOpen = true;
        }

        /// <summary>
        /// Force-hide without clearing the session dismissal. Used on service connect/disconnect
        /// so the panel stays hidden until the next tunnel_status decides.
        /// </summary>
        public void Hide() {
            IsOpen = false;
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
