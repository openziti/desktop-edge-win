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
using System.Windows;
using System.Windows.Controls;

using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;
using NLog;
using SWM = System.Windows.Media;
using ZitiDesktopEdge.DataStructures;
using System.Diagnostics;
using System.Windows.Media;

using WinForms=System.Windows.Forms;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Input;


namespace ZitiDesktopEdge {
    /// <summary>
    /// User Control to list Identities and give status
    /// </summary>
    public partial class IdentityItem : System.Windows.Controls.UserControl {

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public delegate void OnAuthenticate(ZitiIdentity identity);
        public event OnAuthenticate AuthenticateTOTP;
        public delegate void OnEnableMFA(ZitiIdentity identity);
        public event OnEnableMFA EnableMFARequested;
        public delegate void OnIdentityChanged(ZitiIdentity identity);
        public event OnIdentityChanged IdentityChanged;

        public Action<string, string> ShowError;

        public ZitiIdentity _identity;
        public IdentityViewModel IdentityViewModel { get; private set; }
        public ZitiIdentity Identity {
            get {
                return _identity;
            }
            set {
                _identity = value;
                if (IdentityViewModel != null) IdentityViewModel.IdentityChanged -= OnViewModelIdentityChanged;
                IdentityViewModel = new IdentityViewModel(value);
                IdentityViewModel.IdentityChanged += OnViewModelIdentityChanged;
                DataContext = IdentityViewModel;
                ToggleSwitch.Enabled = value.IsEnabled;
            }
        }

        private void OnViewModelIdentityChanged(ZitiIdentity id) {
            IdentityChanged?.Invoke(id);
        }

        /// <summary>
        /// Object constructor, setup the events for the control
        /// </summary>
        public IdentityItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleIdentity;
        }

        public void StopTimers() {
            IdentityViewModel?.StopTimers();
        }

        async private void ToggleIdentity(bool on) {
            try {
                await IdentityViewModel.SetEnabledAsync(on);
            } catch (ServiceException se) {
                MessageBox.Show(se.AdditionalInfo, se.Message);
            } catch (Exception ex) {
                MessageBox.Show("Error", ex.Message);
            }
        }

        private void MainGrid_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
            OverState.Opacity = 0.2;
        }

        private void MainGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            OverState.Opacity = 0;
        }

        private void OpenDetails(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Right) {
                IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                deets.SelectedIdentity = this;
                deets.Identity = this.Identity;
            }
        }

        private void MFAAuthenticate(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (Identity.IsEnabled) {
                this.AuthenticateTOTP?.Invoke(_identity);
            }
        }

        private void ToggledSwitch(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            ToggleSwitch.Toggle();
        }

        private void DoMFAOrOpen(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (MfaRequired.Visibility == Visibility.Visible ||
                TimerCountdown.Visibility == Visibility.Visible ||
                PostureTimedOut.Visibility == Visibility.Visible) {
                MFAAuthenticate(sender, e);
            } else if (ExtAuthRequired.Visibility == Visibility.Visible) {
                ShowExtAuthList(sender, e);
            } else if (_identity.IsEnabled && _identity.IsMFANeeded && !_identity.IsMFAEnabled) {
                // "enable mfa" bubble: open details so the enrollment callback targets this identity, then start enrollment.
                OpenDetails(sender, e);
                EnableMFARequested?.Invoke(_identity);
            } else {
                OpenDetails(sender, e);
            }
        }

        private async void CompleteExtAuth(string provider) {
            try {
                await IdentityViewModel.CompleteExternalAuthAsync(provider);
            } catch (Exception ex) {
                logger.Error("external auth failed: [{}]", ex.Message);
                ShowError("Failed to Authenticate", ex.Message);
            }
        }

        private async void CompleteDefaultExtAuth(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            try {
                if(!_identity.NeedsExtAuth) {
                    return;
                }
                if (_identity?.ExtAuthProviders?.Count > 0) {
                    try {
                        string defaultProvider = _identity.GetDefaultProviderId();
                        await IdentityViewModel.CompleteExternalAuthAsync(defaultProvider);
                    } catch (Exception ex) {
                        ShowError("Unexpected Error", "Please report this issue: " + ex.Message);
                        logger.Error("external auth failed: [{}]", ex.Message);
                    }
                } else {
                    ShowError("Failed to Authenticate", "No external providers found! This is a configuration error. Inform your network administrator.");
                }
            } catch (Exception ex) {
                logger.Error("unexpected error!", ex);
                ShowError("UNEXPECTED ERROR", "Please report this issue: " + ex.Message);
                _identity.AuthInProgress = false;
            }
        }

        private void ShowExtAuthList(object sender, System.Windows.Input.MouseEventArgs e) {
            if (!_identity.NeedsExtAuth) {
                return;
            }
            IconContext.IsEnabled = false;
            IconContext.Visibility = Visibility.Collapsed;
            string defaultProvider = _identity.GetDefaultProvider();
            var fe = sender as FrameworkElement;
            if (fe?.ContextMenu != null && defaultProvider == null) {
                if (_identity?.ExtAuthProviders?.Count > 1) {
                    IconContext.IsEnabled = true;
                    IconContext.Visibility = Visibility.Visible;

                    var contextMenu = fe.ContextMenu;
                    contextMenu.Items.Clear();

                    // Add menu items dynamically
                    _identity.ExtAuthProviders.Sort();
                    foreach (var provider in _identity.ExtAuthProviders) {
                        var menuItem = new MenuItem();
                        menuItem.Click += (s, mouseEventArgs) => {
                            CompleteExtAuth(provider);
                        };
                        menuItem.Header = provider;

                        var controlTemplate = (ControlTemplate)this.FindResource("IdentityItemContextMenuTemplate");
                        menuItem.Template = controlTemplate;
                        contextMenu.Items.Add(menuItem);
                    }
                } else if (_identity?.ExtAuthProviders?.Count == 1) {
                    CompleteExtAuth(_identity?.ExtAuthProviders[0]);
                } else {
                    CompleteExtAuth(null);
                }
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.IsOpen = true;
            } else {
                CompleteDefaultExtAuth(sender, e as MouseButtonEventArgs);
            }
        }
    }
}
