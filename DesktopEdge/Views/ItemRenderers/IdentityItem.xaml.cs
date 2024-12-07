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

namespace ZitiDesktopEdge {
    /// <summary>
    /// User Control to list Identities and give status
    /// </summary>
    public partial class IdentityItem : System.Windows.Controls.UserControl {

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public delegate void StatusChanged(bool attached);
        public event StatusChanged OnStatusChanged;
        public delegate void OnAuthenticate(ZitiIdentity identity);
        public event OnAuthenticate AuthenticateTOTP;
        public delegate void OnIdentityChanged(ZitiIdentity identity);
        public event OnIdentityChanged IdentityChanged;
        public delegate void OnBlurb(ZitiIdentity identity);
        public event OnBlurb BlurbEvent;

        public Action<string, string> ShowError; 
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.Timer _timingTimer;
        private float countdown = -1;
        private float countdownComplete = -1;
        private int available = 0;

        private static SWM.Color mfaOrange = SWM.Color.FromRgb(0xA1, 0x8B, 0x10);
        private static SWM.Color defaultBlue = SWM.Color.FromRgb(0x00, 0x68, 0xF9);
        private static SWM.Color disabledGray = SWM.Color.FromArgb(0xFF, 0xA9, 0xA9, 0xA9);
        private static SWM.Brush MFANeededBrush = new SWM.SolidColorBrush(mfaOrange);
        private static SWM.Brush DefaultBrush = new SWM.SolidColorBrush(defaultBlue);
        private static SWM.Brush DisabledBrush = new SWM.SolidColorBrush(disabledGray);

        public ZitiIdentity _identity;
        public ZitiIdentity Identity {
            get {
                return _identity;
            }
            set {
                _identity = value;
                this.RefreshUI();
            }
        }

        /// <summary>
        /// Object constructor, setup the events for the control
        /// </summary>
        public IdentityItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleIdentity;
        }

        public void StopTimers() {
            _timer?.Stop();
            _timingTimer?.Stop();
        }

        public int GetMaxTimeout() {
            int maxto = -1;
            for (int i = 0; i < _identity.Services.Count; i++) {
                ZitiService info = _identity.Services[i];

                if (info.TimeoutCalculated > -1) {
                    if (info.TimeoutCalculated == 0) {
                        available--;
                    }
                    if (info.TimeoutCalculated > maxto) maxto = info.TimeoutCalculated;

                }
                logger.Trace("Max: " + _identity.Name + " " + maxto + " " + info.Name + " " + info.Timeout + " " + info.TimeoutCalculated + " " + info.TimeoutRemaining + " " + info.TimeUpdated + " " + DateTime.Now);
            }
            return maxto;
        }
        public int GetMinTimeout() {
            int minto = int.MaxValue;
            for (int i = 0; i < _identity.Services.Count; i++) {
                ZitiService info = _identity.Services[i];
                if (info.TimeoutCalculated > -1) {
                    if (info.TimeoutCalculated < minto) minto = info.TimeoutCalculated;
                }
                // logger.Trace("Min: " + _identity.Name + " " + minto + " " + info.Name + " " + info.Timeout + " " + info.TimeoutCalculated + " " + info.TimeoutRemaining + " " + info.TimeUpdated+" "+ DateTime.Now);
            }
            if (minto == int.MaxValue) minto = 0;
            return minto;
        }

        private void MFAEnabledAndNeeded() {
            MainArea.Opacity = 0.6;
            ServiceCountAreaLabel.Content = "authorize";

            float maxto = GetMaxTimeout();
            if (maxto > -1) {
                if (maxto > 0) {
                    if (_timer != null) _timer.Stop();
                    countdownComplete = maxto;
                    _timer = new System.Windows.Forms.Timer();
                    _timer.Interval = 1000;
                    _timer.Tick += TimerTicked;
                    _timer.Start();
                    logger.Info("Timer Started for full timout in " + maxto + "  seconds from identity " + _identity.Name + ".");
                } else {
                    //if (maxto == 0) ShowTimedOut();
                }
            }
            float minto = GetMinTimeout();
            logger.Info("Min/Max For " + _identity.Name + " " + minto + " " + maxto);
            if (minto > -1) {
                if (minto > 0) {
                    if (_timingTimer != null) _timingTimer.Stop();
                    countdown = minto;
                    _timingTimer = new System.Windows.Forms.Timer();
                    _timingTimer.Interval = 1000;
                    _timingTimer.Tick += TimingTimerTick;
                    _timingTimer.Start();
                    logger.Info("Timer Started for first timout in " + minto + " seconds from identity " + _identity.Name + " value with " + _identity.MinTimeout + ".");
                } else {
                    if (maxto > 0) {
                        ShowTimeout();
                    }
                }
            }
            logger.Info("MFAEnabledAndNeeded " + _identity.Name + " Min: " + minto + " Max: " + maxto);
        }

        private void MFAEnabledAndNotNeeded() {
            if (_identity.IsTimedOut) {
                ServiceCountAreaLabel.Content = "authorize2";
                MainArea.Opacity = 1.0;
            } else {
                ServiceCountAreaLabel.Content = "authorize3";
            }
        }

        private void MFANotEnabledAndNotNeeded() {
            MainArea.Opacity = 1.0;
        }

        private void MFANotEnabledAndNeeded() {
            ServiceCountAreaLabel.Content = "disabled";
        }

        public void RefreshUI() {
            TimerCountdown.Visibility = Visibility.Collapsed;
            PostureTimedOut.Visibility = Visibility.Collapsed;
            ServiceCountArea.Visibility = Visibility.Collapsed;
            MfaRequired.Visibility = Visibility.Collapsed;
            ExtAuthRequired.Visibility = Visibility.Collapsed;

            ToggleSwitch.Enabled = _identity.IsEnabled;
            MainArea.Opacity = 1.0;

            Action hideMfa = () => {
                PostureTimedOut.Visibility = Visibility.Collapsed;
                MfaRequired.Visibility = Visibility.Collapsed;
            };
            Action showMfa = () => {
                if (_identity.IsTimedOut) {
                    PostureTimedOut.Visibility = Visibility.Visible;
                } else {
                    MfaRequired.Visibility = Visibility.Visible;
                }
                ServiceCountArea.Visibility = Visibility.Collapsed;
                MainArea.Opacity = 0.6;
            };
            Action showBubbles = () => {
                hideMfa();
                ServiceCountArea.Visibility = Visibility.Visible;
            };

            // identity enabled, timed out
            // identity enabled, mfa needed, not enabled yet
            // identity enabled, mfa needed, enabled, but not verified yet
            // identity enabled, mfa needed, enabled, has been verified
            // identity enabled, mfa not needed at all
            // identity enabled, needs external auth

            // identity disabled, timed out <-- not possible
            // identity disabled, mfa needed, not enabled yet
            // identity disabled, mfa needed, enabled, but not verified yet
            // identity disabled, mfa needed, enabled, has been verified
            // identity disabled, mfa not needed at all

            // identity disabled, needs external auth
            if (_identity.IsEnabled) {
                if (_identity.IsMFAEnabled) {
                    if (_identity.IsMFANeeded) {
                        ServiceCount.Content = _identity.Services.Count.ToString();
                        ServiceCountAreaLabel.Content = "authorize";
                        showMfa();
                    } else {
                        ServiceCount.Content = _identity.Services.Count.ToString();
                        ServiceCountAreaLabel.Content = "services";
                        showBubbles();
                        ServiceCountBorder.Background = DefaultBrush;
                    }
                } else {
                    if (_identity.IsMFANeeded) {
                        ServiceCount.Content = "MFA";
                        ServiceCountAreaLabel.Content = "enable";
                        showBubbles();
                        ServiceCountBorder.Background = MFANeededBrush;
                    } else {
                        ServiceCount.Content = _identity.Services.Count.ToString();
                        ServiceCountAreaLabel.Content = "services";
                        showBubbles();
                        ServiceCountBorder.Background = DefaultBrush;
                    }
                }
                if (_identity.NeedsExtAuth) {
                    ServiceCountAreaLabel.Content = "authorize idp";
                    MainArea.Opacity = 0.6;
                    hideMfa();
                    ServiceCountArea.Visibility = Visibility.Collapsed; //hide bubbles
                    ExtAuthRequired.Visibility = Visibility.Visible;
                }
            } else {
                if (_identity.IsMFAEnabled) {
                    ServiceCount.Content = "MFA";
                    ServiceCountAreaLabel.Content = "id disabled";
                    MainArea.Opacity = 0.6;
                    showMfa();
                } else {
                    if (_identity.IsMFANeeded) {
                        ServiceCount.Content = "MFA";
                        ServiceCountAreaLabel.Content = "disabled3";
                        ServiceCountBorder.Background = DisabledBrush;
                        showMfa();
                    } else {
                        ServiceCount.Content = "-";
                        ServiceCountAreaLabel.Content = "id disabled";
                        MainArea.Opacity = 0.6;
                        showBubbles();
                        ServiceCountBorder.Background = DisabledBrush;
                    }
                }
            }

            IdName.Text = _identity.Name;
            IdUrl.Text = _identity.ControllerUrl;
            if (_identity.ContollerVersion != null && _identity.ContollerVersion.Length > 0) IdUrl.Text = _identity.ControllerUrl + " at " + _identity.ContollerVersion;

            ToggleStatus.Content = ((ToggleSwitch.Enabled) ? "ENABLED" : "DISABLED");
        }

        enum IdentityStates {
            NeedsMfa = 1,
            NeedsExtAuth = 2,
            MfaEnabled = 4,
        }

        private void TimingTimerTick(object sender, EventArgs e) {
            available = _identity.Services.Count;
            GetMaxTimeout();
            if (countdown > -1) {
                countdown--;
                logger.Trace("CountDown " + countdown + " seconds from identity " + _identity.Name + ".");
                if (countdown > 0) {
                    TimeSpan t = TimeSpan.FromSeconds(countdown);
                    string answer = t.Seconds + " seconds";
                    if (t.Days > 0) answer = t.Days + " days " + t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
                    else {
                        if (t.Hours > 0) answer = t.Hours + " hours " + t.Minutes + " minutes " + t.Seconds + " seconds";
                        else {
                            if (t.Minutes > 0) answer = t.Minutes + " minutes " + t.Seconds + " seconds";
                        }
                    }
                    if (countdown <= 1200) {
                        ShowTimeout();

                        if (!_identity.WasNotified) {
                            _identity.WasNotified = true;
                            _identity.ShowMFAToast("The services for " + _identity.Name + " will start to time out in " + answer);
                        }
                    }

                    if (available < _identity.Services.Count) MainArea.ToolTip = (_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.";
                    else MainArea.ToolTip = "Some or all of the services will be timing out in " + answer;
                } else {
                    ShowTimeout();
                    MainArea.ToolTip = (_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.";
                    ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
                }
            } else {
                ShowTimeout();
                MainArea.ToolTip = "Some or all of the services have timed out.";
                ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
            }
        }

        private void ShowTimeout() {
            ServiceCountAreaLabel.Content = available + "/" + _identity.Services.Count;
            if (!_identity.WasNotified) {
                if (available < _identity.Services.Count) {
                    _identity.WasNotified = true;
                    _identity.ShowMFAToast((_identity.Services.Count - available) + " of " + _identity.Services.Count + " services have timed out.");
                }
                _identity.IsTimingOut = true;

                this.IdentityChanged?.Invoke(_identity);
            }
        }

        private void ShowTimedOut() {
            _identity.Mutex.Wait();
            if (!_identity.WasFullNotified) {
                _identity.WasFullNotified = true;
                _identity.ShowMFAToast("All of the services with a timeout set for the identity " + _identity.Name + " have timed out");
                RefreshUI();
                if (_timer != null) _timer.Stop();
            }
            _identity.Mutex.Release();
        }

        private void TimerTicked(object sender, EventArgs e) {
            if (countdownComplete > -1) {
                countdownComplete--;
                if (countdownComplete <= 0) ShowTimedOut();
            }
        }

        async private void ToggleIdentity(bool on) {
            try {
                if (OnStatusChanged != null) {
                    OnStatusChanged(on);
                }
                DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
                Identity id = await client.IdentityOnOffAsync(_identity.Identifier, on);
                this.Identity.IsEnabled = on;
                if (on) {
                    ToggleStatus.Content = "ENABLED";
                    Identity.AuthInProgress = false;
                } else {
                    ToggleStatus.Content = "DISABLED";
                }
                RefreshUI();
            } catch (ServiceException se) {
                MessageBox.Show(se.AdditionalInfo, se.Message);
            } catch (Exception ex) {
                MessageBox.Show("Error", ex.Message);
            }
        }

        private void Canvas_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
            OverState.Opacity = 0.2;
        }

        private void Canvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            OverState.Opacity = 0;
        }

        private void OpenDetails(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Right && Identity.IsEnabled) {
                IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                deets.SelectedIdentity = this;
                deets.Identity = this.Identity;
            } else {

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
                CompleteExtAuth(sender, e);
            } else {
                OpenDetails(sender, e);
            }
        }

        private void CompleteExtAuth(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            try {
                if (_identity?.ExtAuthProviders?.Count > 0) {
                    if (_identity.AuthInProgress) {
                        BlurbEvent?.Invoke(_identity);
                    } else {
                        performExtAuth();
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

        async void performExtAuth() {
            _identity.AuthInProgress = true;
             DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            ExternalAuthLoginResponse resp = await client.ExternalAuthLogin(_identity.Identifier, _identity.ExtAuthProviders[0]);
            if (resp?.Error == null) {
                if (resp?.Data?.url != null) {
                    Console.WriteLine(resp.Data?.url);
                    Process.Start(resp.Data.url);
                } else {
                    Console.WriteLine("The response contained no url???");
                }
            } else {
                ShowError("Failed to Authenticate", resp.Error);
                _identity.AuthInProgress = false;
            }
        }

        private void Rectangle_GotFocus(object sender, RoutedEventArgs e) {
            Console.WriteLine("focus");
        }

        private void Rectangle_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
            if (sender is Grid grid) {

                var contextMenu = grid.ContextMenu;
                // Ensure contextMenu is not null
                if (contextMenu != null) {
                    if (_identity?.ExtAuthProviders?.Count > 1) {

                        // Clear previous items
                        contextMenu.Items.Clear();

                        // Add menu items dynamically
                        foreach (var provider in _identity.ExtAuthProviders) {
                            var menuItem = new System.Windows.Controls.MenuItem {
                                Header = provider // Use Header instead of Content
                            };

                            menuItem.Click += (s, args) => {
                                // Handle provider selection here
                                MessageBox.Show($"You selected {provider}");
                            };

                            contextMenu.Items.Add(menuItem);
                        }

                        // Show the context menu
                        contextMenu.IsOpen = true;
                    }
                }
            }
        }

        private void ExternalIdpHover(object sender, System.Windows.Input.MouseEventArgs e) {
            var fe = sender as FrameworkElement;
            if (fe?.ContextMenu != null) {
                if (_identity?.ExtAuthProviders?.Count > 1) {

                    var contextMenu = fe.ContextMenu;
                    // Clear previous items
                    contextMenu.Items.Clear();

                    // Add menu items dynamically
                    foreach (var provider in _identity.ExtAuthProviders) {
                        var menuItem1 = new System.Windows.Controls.MenuItem {
                            Header = provider // Use Header instead of Content
                        };
                        var menuItem = new OZMenuItem();
                        menuItem.LabelCtrl.Content = provider;
                        menuItem.IconCtrl.Visibility = Visibility.Collapsed;
                        menuItem.IconCtrl.Width = 0;
                        menuItem.ChevronCtrl.Width = 0;
                        menuItem.LabelCtrl.Background = Brushes.White;

                        menuItem.MouseUp += (s, args) => {
                            // Handle provider selection here
                            Console.WriteLine("clicked it You selected {provider}");
                        };

                        contextMenu.Items.Add(menuItem);
                    }

                    // Show the context menu
                    contextMenu.IsOpen = true;
                }
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.IsOpen = true;
            }
        }
    }
}
