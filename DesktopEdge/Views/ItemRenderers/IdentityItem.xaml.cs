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
        private MainWindow MainWindow => (MainWindow)Application.Current.MainWindow;
        private IdentityViewModel IdentityViewModel => DataContext as IdentityViewModel;
        public ZitiIdentity Identity => IdentityViewModel?.Identity;

        public IdentityItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleIdentity;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            IdentityViewModel oldViewModel = e.OldValue as IdentityViewModel;
            if (oldViewModel != null) {
                oldViewModel.IdentityChanged -= OnViewModelIdentityChanged;
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            IdentityViewModel newViewModel = e.NewValue as IdentityViewModel;
            if (newViewModel != null) {
                newViewModel.IdentityChanged += OnViewModelIdentityChanged;
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
                ToggleSwitch.Enabled = newViewModel.Identity.IsEnabled;
            }
        }

        // Toggler.Enabled is not bindable, so keep it in step with the VM whenever it re-renders.
        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (IdentityViewModel != null) ToggleSwitch.Enabled = IdentityViewModel.Identity.IsEnabled;
        }

        private void OnViewModelIdentityChanged(ZitiIdentity id) {
            MainWindow.RefreshNotifyIcon();
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
                MainWindow.ShowAuthenticate(Identity);
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
            } else if (Identity.IsEnabled && Identity.IsMFANeeded && !Identity.IsMFAEnabled) {
                // "enable mfa" bubble: open details so the enrollment callback targets this identity, then start enrollment.
                OpenDetails(sender, e);
                MainWindow.IdItem_EnableMFA(Identity);
            } else {
                OpenDetails(sender, e);
            }
        }

        private async void CompleteExtAuth(string provider) {
            try {
                await IdentityViewModel.CompleteExternalAuthAsync(provider);
            } catch (Exception ex) {
                logger.Error("external auth failed: [{}]", ex.Message);
                MainWindow.ShowError("Failed to Authenticate", ex.Message);
            }
        }

        private async void CompleteDefaultExtAuth(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            try {
                if(!Identity.NeedsExtAuth) {
                    return;
                }
                if (Identity?.ExtAuthProviders?.Count > 0) {
                    try {
                        string defaultProvider = Identity.GetDefaultProviderId();
                        await IdentityViewModel.CompleteExternalAuthAsync(defaultProvider);
                    } catch (Exception ex) {
                        MainWindow.ShowError("Unexpected Error", "Please report this issue: " + ex.Message);
                        logger.Error("external auth failed: [{}]", ex.Message);
                    }
                } else {
                    MainWindow.ShowError("Failed to Authenticate", "No external providers found! This is a configuration error. Inform your network administrator.");
                }
            } catch (Exception ex) {
                logger.Error("unexpected error!", ex);
                MainWindow.ShowError("UNEXPECTED ERROR", "Please report this issue: " + ex.Message);
                Identity.AuthInProgress = false;
            }
        }

        private void ShowExtAuthList(object sender, System.Windows.Input.MouseEventArgs e) {
            if (!Identity.NeedsExtAuth) {
                return;
            }
            IconContext.IsEnabled = false;
            IconContext.Visibility = Visibility.Collapsed;
            string defaultProvider = Identity.GetDefaultProvider();
            var fe = sender as FrameworkElement;
            if (fe?.ContextMenu != null && defaultProvider == null) {
                if (Identity?.ExtAuthProviders?.Count > 1) {
                    IconContext.IsEnabled = true;
                    IconContext.Visibility = Visibility.Visible;

                    var contextMenu = fe.ContextMenu;
                    contextMenu.Items.Clear();

                    // Add menu items dynamically
                    Identity.ExtAuthProviders.Sort();
                    foreach (var provider in Identity.ExtAuthProviders) {
                        var menuItem = new MenuItem();
                        menuItem.Click += (s, mouseEventArgs) => {
                            CompleteExtAuth(provider);
                        };
                        menuItem.Header = provider;

                        var controlTemplate = (ControlTemplate)this.FindResource("IdentityItemContextMenuTemplate");
                        menuItem.Template = controlTemplate;
                        contextMenu.Items.Add(menuItem);
                    }
                } else if (Identity?.ExtAuthProviders?.Count == 1) {
                    CompleteExtAuth(Identity?.ExtAuthProviders[0]);
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
