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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System;
using System.Reflection;
using System.Net.Mail;
using System.IO;
using NLog;
using System.Windows.Media.Animation;

using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;
using System.Configuration;
using Ziti.Desktop.Edge.Models;
using System.Threading;
using System.Threading.Tasks;
using ZitiDesktopEdge.Utility;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MainMenu.xaml
    /// </summary>
    public partial class MainMenu : UserControl {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public delegate void AttachementChanged(bool attached);
        public event AttachementChanged OnAttachmentChange;
        public delegate Task<bool> LogLevelChanged(string level);
        public event LogLevelChanged OnLogLevelChanged;
        public delegate void Detched(MouseButtonEventArgs e);
        public event Detched HandleAttachment;
        public delegate void ShowBlurb(string message);
        public event ShowBlurb OnShowBlurb;
        public string menuState = "Main";
        public string licenseData = "it's open source.";
        public string LogLevel = "";
        private string appVersion = null;
        public double MainHeight = 500;

        private ZDEWViewState state;

        public bool ShowUnexpectedFailure { get; set; }

        public void ShowUpdateAvailable() {
            if (state.UpdateAvailable) {
                ForceUpdate.Visibility = Visibility.Visible;
            }

            if (state.PendingUpdate.TimeLeft > 0) {
                UpdateTimeLeft.Visibility = Visibility.Visible;
                if (!state.AutomaticUpdatesDisabled) {
                    UpdateTimeLeft.Content = $"Automatic update to {state.PendingUpdate.Version} will occur on or after {state.PendingUpdate.InstallTime.ToString("g")}";
                    CheckForUpdateStatus.Content = $"update {state.PendingUpdate.Version} is available";
                } else {
                    UpdateTimeLeft.Content = "";
                }
                CheckForUpdateStatus.Visibility = UpdateTimeLeft.Visibility;
            }
            SetAutomaticUpgradesState();
        }

        internal MainWindow MainWindow { get; set; }

        public MainMenu() {
            InitializeComponent();
            Application.Current.MainWindow.Title = "Ziti Desktop Edge";
            state = (ZDEWViewState)Application.Current.Properties["ZDEWViewState"];

            try {
                ShowUnexpectedFailure = bool.Parse(ConfigurationManager.AppSettings.Get("ShowUnexpectedFailure"));
            } catch {
                //if we can't parse the config - leave it as false...
                ShowUnexpectedFailure = false; //setting it here in case anyone changes the default above
            }

            appVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            LicensesItems.Text = licenseData;
            // don't check from the UI any more... CheckUpdates();
        }

        private void HideMenu(object sender, MouseButtonEventArgs e) {
            menuState = "Menu";
            UpdateState();
            MainMenuArea.Visibility = Visibility.Collapsed;
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
            HandleAttachment(e);
        }

        private void CloseApp(object sender, MouseButtonEventArgs e) {
            Application.Current.Shutdown();
        }

        private void ShowAbout(object sender, MouseButtonEventArgs e) {
            menuState = "About";
            UpdateState();
        }

        private void ShowAdvanced(object sender, MouseButtonEventArgs e) {
            menuState = "Advanced";
            UpdateState();
        }
        private void ShowIdentities(object sender, MouseButtonEventArgs e) {
            menuState = "Identities";
            UpdateState();
        }
        private void ShowLicenses(object sender, MouseButtonEventArgs e) {
            menuState = "Licenses";
            UpdateState();
        }
        private void ShowConfig(object sender, MouseButtonEventArgs e) {
            menuState = "Config";
            UpdateState();
        }
        private void ShowLogs(object sender, MouseButtonEventArgs e) {
            menuState = "Logs";
            UpdateState();
        }
        private void ShowUILogs(object sender, MouseButtonEventArgs e) {
            menuState = "UILogs";
            UpdateState();
        }
        private void ShowReleaseStreamMenuAction(object sender, MouseButtonEventArgs e) {
            logger.Warn("this is ShowReleaseStreamMenuAction at warn");
            logger.Info("this is ShowReleaseStreamMenuAction at info");
            logger.Debug("this is ShowReleaseStreamMenuAction at debug");
            logger.Trace("this is ShowReleaseStreamMenuAction at trace");
            menuState = "SetReleaseStream";
            UpdateState();
        }

        private void ShowAutomaticUpgradesMenuAction(object sender, MouseButtonEventArgs e) {
            menuState = "ConfigureAutomaticUpgrades";
            UpdateState();
        }

        async private void SetAutomaticUpgradesMenuAction(object sender, MouseButtonEventArgs e) {
            bool disableAutomaticUpgrades = false;
            if (sender == AutomaticUpgradesItemOff) {
                disableAutomaticUpgrades = true;
            }
            var monitorClient = (MonitorClient)Application.Current.Properties["MonitorClient"];
            try {
                SvcResponse r = await monitorClient.SetAutomaticUpgradeDisabledAsync(disableAutomaticUpgrades);
                if (r.Code != 0) {
                    logger.Error(r?.Error);
                }
            } catch (MonitorServiceException) {
                MainWindow.ShowError("Could Not Set Automatic Update", "The monitor service is offline");
            } catch (Exception ex) {
                logger.Error("unexpected error when setting automatic upgrade enabled", ex);
            }
            SetAutomaticUpgradesState();
        }

        private void checkResponse(SvcResponse r, string titleOnErr, string msgOnErr) {
            if (r == null) {
                MainWindow.ShowError(titleOnErr, msgOnErr);
            } else {
                logger.Info(r?.ToString());
            }
        }

        private void SetLogLevel(object sender, MouseButtonEventArgs e) {
            menuState = "LogLevel";
            UpdateState();
        }

        private void UpdateState() {
            double h = this.ActualHeight - 100.00;
            if (h > 0) {
                IdListScrollView.Height = h;
            }
            IdListScrollView.Visibility = Visibility.Collapsed;
            MainItems.Visibility = Visibility.Collapsed;
            AboutItems.Visibility = Visibility.Collapsed;
            MainItemsButton.Visibility = Visibility.Collapsed;
            AboutItemsArea.Visibility = Visibility.Collapsed;
            BackArrow.Visibility = Visibility.Collapsed;
            AdvancedItems.Visibility = Visibility.Collapsed;
            LicensesItems.Visibility = Visibility.Collapsed;
            LogsItems.Visibility = Visibility.Collapsed;
            ConfigItems.Visibility = Visibility.Collapsed;
            LogLevelItems.Visibility = Visibility.Collapsed;
            AutomaticUpgradesItems.Visibility = Visibility.Collapsed;
            Visibility visibilityFromUpdateAvail = state.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
            TriggerUpdateButton.Visibility = visibilityFromUpdateAvail;
            ForceUpdate.Visibility = visibilityFromUpdateAvail;
            CheckForUpdateStatus.Visibility = visibilityFromUpdateAvail;

            if (menuState == "About") {
                MenuTitle.Content = "About";
                AboutItemsArea.Visibility = Visibility.Visible;
                AboutItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;

                string version = "";
                try {
                    TunnelStatus s = (TunnelStatus)Application.Current.Properties["CurrentTunnelStatus"];
                    version = $"{s.ServiceVersion.Version}@{s.ServiceVersion.Revision}";
                } catch (Exception e) {
                    logger.Warn(e, "Could not get service version/revision?");
                }

                // Interface Version
                VersionInfo.Content = $"App: {appVersion} Service: {version}";

            } else if (menuState == "Advanced") {
                MenuTitle.Content = "ADVANCED SETTINGS";
                AdvancedItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "Licenses") {
                MenuTitle.Content = "THIRD PARTY LICENSES";
                LicensesItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "Logs") {
                MenuTitle.Content = "ADVANCED SETTINGS";
                AdvancedItems.Visibility = Visibility.Visible;
                //string targetFile = NativeMethods.GetFinalPathName(MainWindow.ExpectedLogPathServices);
                string targetFile = MainWindow.ExpectedLogPathServices;

                OpenLogFile("service", targetFile);
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "UILogs") {
                MenuTitle.Content = "ADVANCED SETTINGS";
                AdvancedItems.Visibility = Visibility.Visible;
                OpenLogFile("UI", MainWindow.ExpectedLogPathUI);
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "LogLevel") {
                ResetLevels();

                MenuTitle.Content = "SET LOG LEVEL";
                LogLevelItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "ConfigureAutomaticUpgrades") {
                SetAutomaticUpgradesState();

                MenuTitle.Content = "AUTOMATIC UPGRADES";
                AutomaticUpgradesItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;
            } else if (menuState == "Config") {
                MenuTitle.Content = "TUNNEL CONFIGURATION";
                ConfigItems.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;

                ConfigPageSize.Value = ((Application.Current.Properties.Contains("ApiPageSize")) ? Application.Current.Properties["ApiPageSize"].ToString() : "25");
                ConfigIp.Value = Application.Current.Properties["ip"]?.ToString();
                ConfigSubnet.Value = Application.Current.Properties["subnet"]?.ToString();
                ConfigMtu.Value = Application.Current.Properties["mtu"]?.ToString();
                ConfigDns.Value = Application.Current.Properties["dns"]?.ToString();
                ConfigDnsEnabled.Value = Application.Current.Properties["dnsenabled"]?.ToString();
            } else if (menuState == "Identities") {
                MenuTitle.Content = "IDENTITIES";
                IdListScrollView.Visibility = Visibility.Visible;
                BackArrow.Visibility = Visibility.Visible;
            } else {
                MenuTitle.Content = "MAIN MENU";
                MainItems.Visibility = Visibility.Visible;
                MainItemsButton.Visibility = Visibility.Visible;
            }

            // ShowUpdateAvailable();
        }

        private void OpenLogFile(string which, string logFile) {
            var whichRoot = Path.Combine(MainWindow.ExpectedLogPathRoot, which);
            try {
                string target = Native.NativeMethods.GetFinalPathName(logFile);
                if (File.Exists(target)) {
                    logger.Info("opening {0} logs at: {1}", which, target);
                    var p = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    if (p != null) {
                        logger.Info("showing {0} logs. file: {1}", which, target);
                    } else {
                        Process.Start(whichRoot);
                    }
                    return;
                } else {
                    logger.Warn("could not show {0} logs. file not found: {1}", which, target);
                }
            } catch {
            }
            Process.Start(whichRoot);
        }

        private void GoBack(object sender, MouseButtonEventArgs e) {
            if (menuState == "Config" || menuState == "LogLevel" || menuState == "UILogs" || menuState == "SetReleaseStream" || menuState == "ConfigureAutomaticUpgrades") {
                menuState = "Advanced";
            } else if (menuState == "Licenses") {
                menuState = "About";
            } else {
                menuState = "Menu";
            }
            UpdateState();
        }
        private void ShowPrivacy(object sender, MouseButtonEventArgs e) {
            Process.Start(new ProcessStartInfo("https://netfoundry.io/privacy") { UseShellExecute = true });
        }
        private void ShowTerms(object sender, MouseButtonEventArgs e) {
            Process.Start(new ProcessStartInfo("https://netfoundry.io/terms") { UseShellExecute = true });
        }

        async public void CollectFeedbackLogs(object sender, MouseButtonEventArgs e) {
            try {
                MainWindow.ShowLoad("Collecting Information", "Please wait while we run some commands\nand collect some diagnostic information");

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("Logs collected at : " + DateTime.Now.ToString());
                sb.Append(". client version : " + appVersion);

                string timestamp = DateTime.Now.ToFileTime().ToString();

                var dataClient = (DataClient)Application.Current.Properties["ServiceClient"];

                string exeLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string logLocation = Path.Combine(exeLocation, "logs");
                string serviceLogsLocation = Path.Combine(logLocation, "service");
                await dataClient.zitiDump(serviceLogsLocation);

                var monitorClient = (MonitorClient)Application.Current.Properties["MonitorClient"];
                MonitorServiceStatusEvent resp = await monitorClient.CaptureLogsAsync();
                if (resp == null) {
                    logger.Error("no response from monitorClient?");
                    MainWindow mw = (MainWindow)Application.Current.MainWindow;
                    mw?.ShowError("Error Collecting Feedback", "An error occurred while trying to gather feedback.\nIs the monitor service running?");
                    return;
                }
                string pathToLogs = resp.Message;
                logger.Info("Log files found at : {0}", resp.Message);
                string args = string.Format("/Select, \"{0}\"", pathToLogs);

                ProcessStartInfo pfi = new ProcessStartInfo("Explorer.exe", args);
                Process.Start(pfi);
            } catch (MonitorServiceException) {
                MainWindow.ShowError("Could Not Collect Feedback", "The monitor service is offline");
            } catch (Exception ex) {
                logger.Warn(ex, "An unexpected error has occurred when submitting feedback? {0}", ex.Message);
                MainWindow.ShowError("Could Not Collect Feedback", "The monitor service is offline");
            }
            MainWindow.HideLoad();
        }

        private void ShowSupport(object sender, MouseButtonEventArgs e) {
            Process.Start(new ProcessStartInfo("https://openziti.discourse.group/") { UseShellExecute = true });
        }

        private void DetachWindow(object sender, MouseButtonEventArgs e) {
            Application.Current.MainWindow.ShowInTaskbar = true;
            DetachButton.Visibility = Visibility.Collapsed;
            AttachButton.Visibility = Visibility.Visible;
            Arrow.Visibility = Visibility.Collapsed;
            if (OnAttachmentChange != null) {
                OnAttachmentChange(false);
            }
            MainMenuArea.Visibility = Visibility.Collapsed;
        }

        public void Detach() {
            Application.Current.MainWindow.ShowInTaskbar = true;
            DetachButton.Visibility = Visibility.Collapsed;
            AttachButton.Visibility = Visibility.Visible;
            Arrow.Visibility = Visibility.Collapsed;
        }
        public void Retach() {
            Application.Current.MainWindow.ShowInTaskbar = false;
            DetachButton.Visibility = Visibility.Visible;
            AttachButton.Visibility = Visibility.Collapsed;
            Arrow.Visibility = Visibility.Visible;
        }
        private void RetachWindow(object sender, MouseButtonEventArgs e) {
            Application.Current.MainWindow.ShowInTaskbar = false;
            DetachButton.Visibility = Visibility.Visible;
            AttachButton.Visibility = Visibility.Collapsed;
            Arrow.Visibility = Visibility.Visible;
            if (OnAttachmentChange != null) {
                OnAttachmentChange(true);
            }
        }

        private void ResetLevels() {
            if (this.LogLevel == "") this.LogLevel = "error";
            LogVerbose.IsSelected = false;
            LogDebug.IsSelected = false;
            LogInfo.IsSelected = false;
            LogError.IsSelected = false;
            LogWarn.IsSelected = false;
            LogTrace.IsSelected = false;
            if (this.LogLevel == "verbose") LogVerbose.IsSelected = true;
            else if (this.LogLevel == "debug") LogDebug.IsSelected = true;
            else if (this.LogLevel == "info") LogInfo.IsSelected = true;
            else if (this.LogLevel == "error") LogError.IsSelected = true;
            else if (this.LogLevel == "warn") LogWarn.IsSelected = true;
            else if (this.LogLevel == "trace") LogTrace.IsSelected = true;
        }

        async private void SetLevel(object sender, MouseButtonEventArgs e) {
            SubOptionItem item = (SubOptionItem)sender;
            if (OnLogLevelChanged != null) {
                if (await OnLogLevelChanged(item.Label.ToLower())) {
                    this.LogLevel = item.Label.ToLower();
                }
            }
            ResetLevels();
        }

        private void SetAutomaticUpgradesState() {
            bool disabled = state.AutomaticUpdatesDisabled;
            this.AutomaticUpgradesItemOn.IsSelected = !disabled;
            this.AutomaticUpgradesItemOff.IsSelected = disabled;
            this.UpdateUrl.Text = state.AutomaticUpdateURL;
        }

        async private void CheckForUpdate_OnClick(object sender, MouseButtonEventArgs e) {
            logger.Info("checking for update...");
            CheckForUpdateStatus.Content = "Checking for updates...";
            await Task.Delay(1000);
            if (state.AutomaticUpdateURL != this.UpdateUrl.Text) {
                SetUpdateUrlButton_Click(sender, e);
            }
            try {
                CheckForUpdate.IsEnabled = false;
                CheckForUpdateStatus.Visibility = Visibility.Visible;
                var monitorClient = (MonitorClient)Application.Current.Properties["MonitorClient"];
                var r = await monitorClient.DoUpdateCheck();
                checkResponse(r, "Error When Checking for Update", "An error occurred while trying check for update.");
                CheckForUpdateStatus.Content = r.Message;
                if (r.UpdateAvailable) {
                    TriggerUpdateButton.Visibility = Visibility.Visible;
                    ForceUpdate.Visibility = Visibility.Visible;
                } else {
                    TriggerUpdateButton.Visibility = Visibility.Collapsed;
                    ForceUpdate.Visibility = Visibility.Collapsed;
                }
            } catch (MonitorServiceException) {
                CheckForUpdateStatus.Content = "Monitor service is offline";
            } catch (Exception ex) {
                logger.Error(ex, "unexpected error in update check: {0}", ex.Message);
                CheckForUpdateStatus.Content = "Error checking for updates. See logs.";
            }
            CheckForUpdate.IsEnabled = true;
        }

        async private void TriggerUpdate_RoutedEventArgs_Click(object sender, RoutedEventArgs e) {
            Button src = null;
            if (sender is Button) {
                src = sender as Button;
            }
            try {
                CheckForUpdateStatus.Content = "Requesting automatic update...";
                var monitorClient = (MonitorClient)Application.Current.Properties["MonitorClient"];
                var r = await monitorClient.TriggerUpdate();
                CheckForUpdateStatus.Content = "Automatic update requested...";
                if (r == null) {
                    MainWindow.ShowError("Error When Triggering Update", "An error occurred while trying to trigger the update.");
                    if (src != null) src.IsEnabled = true;
                } else {
                    this.OnShowBlurb?.Invoke("Update Requested");
                    if (src != null) src.Content = "Request Update Again";
                    UpdateTimeLeft.Content = "Update Requested at " + DateTime.Now;
                    TriggerUpdateButton.Visibility = Visibility.Collapsed;
                    logger.Info(r?.ToString());
                    menuState = "Menu";
                    UpdateState();
                    MainMenuArea.Visibility = Visibility.Collapsed;
                }
            } catch (Exception ex) {
                logger.Error(ex, "unexpected error in update check: {0}", ex.Message);
                MainWindow.ShowError("Error When Triggering Update", "An error occurred while trying to trigger the update.");
                if (src != null) src.IsEnabled = true;
            }
        }

        public void SetupIdList(ZitiIdentity[] ids) {
            IdListView.Children.Clear();
            for (int i = 0; i < ids.Length; i++) {
                MenuIdentityItem item = new MenuIdentityItem();
                item.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                item.Label = ids[i].Name;
                item.Identity = ids[i];
                item.ToggleSwitch.Enabled = ids[i].IsEnabled;
                IdListView.Children.Add(item);
            }
        }

        public void SetAppUpgradeAvailableText(string msg) {
            this.Dispatcher.Invoke(() => {
                VersionOlder.Content = msg;
                VersionNewer.Content = "";
                VersionOlder.Visibility = Visibility.Visible;
                VersionNewer.Visibility = Visibility.Collapsed;
            });
        }
        public void SetAppIsNewer(string msg) {
            this.Dispatcher.Invoke(() => {
                VersionNewer.Content = msg;
                VersionOlder.Content = "";
                VersionNewer.Visibility = Visibility.Visible;
                VersionOlder.Visibility = Visibility.Collapsed;
            });
        }

        public void Disconnected() {
            ConfigItems.IsEnabled = false;
            ConfigItems.Opacity = 0.3;
            LogLevelItems.IsEnabled = false;
            LogLevelItems.Opacity = 0.3;
        }

        public void Connected() {
            ConfigItems.IsEnabled = true;
            ConfigItems.Opacity = 1.0;
            LogLevelItems.IsEnabled = true;
            LogLevelItems.Opacity = 1.0;
        }

        /// <summary>
        /// Save the config information to the properties and queue for update.
        /// </summary>
        async private void UpdateConfig() {
            logger.Info("updating config...");
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            try {
                ComboBoxItem item = (ComboBoxItem)ConfigMaskNew.SelectedValue;
                var newMaskVar = Int32.Parse(item.Tag.ToString());
                var addDnsNewVar = Convert.ToBoolean(AddDnsNew.IsChecked);
                CheckRange();
                int pageSize = Int32.Parse(ConfigePageSizeNew.Text);
                ConfigPageSize.Value = ConfigePageSizeNew.Text;

                var r = await client.UpdateConfigAsync(ConfigIpNew.Text, newMaskVar, addDnsNewVar, pageSize);
                if (r.Code != 0) {
                    this.OnShowBlurb?.Invoke("Error: " + r.Error);
                    logger.Debug("ERROR: {0} : {1}", r.Message, r.Error);
                } else {
                    this.OnShowBlurb?.Invoke("Config Save, Please Restart Ziti to Update");
                    this.CloseEdit();
                }
                logger.Info("Got response from update config task : {0}", r);
            } catch (DataStructures.ServiceException se) {
                this.OnShowBlurb?.Invoke("Error: " + se.Message);
                logger.Error(se, "service exception in update check: {0}", se.Message);
            } catch (Exception ex) {
                this.OnShowBlurb?.Invoke("Error: " + ex.Message);
                logger.Error(ex, "unexpected error in update check: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Save the frequency information to the properties and queue for update.
        /// </summary>
        async private void UpdateFrequency() {
            logger.Info("updating frequency...");
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            try {
                var newFrequencyVar = Int32.Parse(Frequency.Text);

                var r = await client.NotificationFrequencyPayloadAsync(newFrequencyVar);
                if (r.Code != 0) {
                    this.OnShowBlurb?.Invoke("Error: " + r.Error);
                    logger.Debug("ERROR: {0} : {1}", r.Message, r.Error);
                } else {
                    this.OnShowBlurb?.Invoke("Frequency Saved");
                    this.CloseFrequency();
                }
                logger.Info("Got response from update frequency task : {0}", r);
            } catch (DataStructures.ServiceException se) {
                this.OnShowBlurb?.Invoke("Error: " + se.Message);
                logger.Error(se, "service exception in update frequency check: {0}", se.Message);
            } catch (Exception ex) {
                this.OnShowBlurb?.Invoke("Error: " + ex.Message);
                logger.Error(ex, "unexpected error in update frequency check: {0}", ex.Message);
            }
        }


        /// <summary>
        /// Show the Edit Modal and blur the background
        /// </summary>
        private void ShowEdit_Click(object sender, MouseButtonEventArgs e) {
            ConfigIpNew.Text = ConfigIp.Value;
            ConfigePageSizeNew.Text = ConfigPageSize.Value;
            CheckRange();
            for (int i = 0; i < ConfigMaskNew.Items.Count; i++) {
                ComboBoxItem item = (ComboBoxItem)ConfigMaskNew.Items.GetItemAt(i);
                if (item.Content.ToString().IndexOf(ConfigSubnet.Value) > 0) {
                    ConfigMaskNew.SelectedIndex = i;
                    break;
                }
            }
            AddDnsNew.IsChecked = false;
            if (Application.Current.Properties.Contains("dnsenabled")) {
                AddDnsNew.IsChecked = (bool)Application.Current.Properties["dnsenabled"];
            }
            EditArea.Opacity = 0;
            EditArea.Visibility = Visibility.Visible;
            EditArea.Margin = new Thickness(0, 0, 0, 0);
            EditArea.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(.3)));
            EditArea.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));
            ShowModal();
        }

        /// <summary>
        /// Show the Frequency Modal and blur the background
        /// </summary>
        private void ShowFrequency() {
            Frequency.Text = "";
            FrequencyArea.Opacity = 0;
            FrequencyArea.Visibility = Visibility.Visible;
            FrequencyArea.Margin = new Thickness(0, 0, 0, 0);
            FrequencyArea.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(.3)));
            FrequencyArea.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));
            ShowModal();
        }

        /// <summary>
        /// Hide the Edit Config
        /// </summary>
        private void CloseFrequency() {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
            animation.Completed += CloseFrequencyComplete;
            FrequencyArea.BeginAnimation(Grid.OpacityProperty, animation);
            FrequencyArea.BeginAnimation(Grid.MarginProperty, animateThick);
            HideModal();
        }

        /// <summary>
        /// Close the config window
        /// </summary>
        /// <param name="sender">The close button</param>
        /// <param name="e">The event arguments</param>
        private void CloseFrequencyComplete(object sender, EventArgs e) {
            FrequencyArea.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Hide the Edit Config
        /// </summary>
        private void CloseEdit() {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
            animation.Completed += CloseComplete;
            EditArea.BeginAnimation(Grid.OpacityProperty, animation);
            EditArea.BeginAnimation(Grid.MarginProperty, animateThick);
            HideModal();
        }

        /// <summary>
        /// Show the modal, aniimating opacity
        /// </summary>
        private void ShowModal() {
            ModalBg.Visibility = Visibility.Visible;
            ModalBg.Opacity = 0;
            DoubleAnimation animation = new DoubleAnimation(.8, TimeSpan.FromSeconds(.3));
            ModalBg.BeginAnimation(Grid.OpacityProperty, animation);
        }

        /// <summary>
        /// Close the config window
        /// </summary>
        /// <param name="sender">The close button</param>
        /// <param name="e">The event arguments</param>
        private void CloseComplete(object sender, EventArgs e) {
            EditArea.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Hide the modal animating the opacity
        /// </summary>
        private void HideModal() {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            animation.Completed += ModalHideComplete;
            ModalBg.BeginAnimation(Grid.OpacityProperty, animation);
        }

        /// <summary>
        /// When the animation completes, set the visibility to avoid UI object conflicts
        /// </summary>
        /// <param name="sender">The animation</param>
        /// <param name="e">The event</param>
        private void ModalHideComplete(object sender, EventArgs e) {
            ModalBg.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Close the config editor without saving
        /// </summary>
        /// <param name="sender">The image button</param>
        /// <param name="e">The click event</param>
        private void CloseEditConfig(object sender, MouseButtonEventArgs e) {
            CloseEdit();
        }

        private void SaveConfig_Click(object sender, MouseButtonEventArgs e) {
            this.UpdateConfig();
        }

        private void SaveFrequencyButton_OnClick(object sender, MouseButtonEventArgs e) {
            UpdateFrequency();
        }

        private void CloseFrequencyArea(object sender, MouseButtonEventArgs e) {
            CloseFrequency();
        }

        private void EditFreqButton_OnClick(object sender, MouseButtonEventArgs e) {
            ShowFrequency();
        }

        private void Frequency_KeyUp(object sender, KeyEventArgs e) {
            if (e.Key == Key.Return) {
                UpdateFrequency();
            }
        }

        private void ConfigePageSizeNew_KeyDown(object sender, KeyEventArgs e) {
        }

        private void ConfigePageSizeNew_LostFocus(object sender, RoutedEventArgs e) {
            CheckRange();
        }

        private void CheckRange() {
            int defaultVal = 250;
            string setVal = ConfigePageSizeNew.Text;
            int value = defaultVal;
            if (Int32.TryParse(ConfigePageSizeNew.Text, out value)) {
            }
            if (value < 10 || value > 500) value = defaultVal;
            ConfigePageSizeNew.Text = value.ToString();
        }

        private void ResetUrlButton_Click(object sender, RoutedEventArgs e) {
            UpdateUrl.Text = GithubAPI.ProdUrl;
        }

        private async void SetUpdateUrlButton_Click(object sender, MouseButtonEventArgs e) {
            try {
                var monitorClient = (MonitorClient)Application.Current.Properties["MonitorClient"];

                SvcResponse r = await monitorClient.SetAutomaticUpgradeURLAsync(UpdateUrl.Text);
                if (r == null) {
                    logger.Error("Failed to set automatic upgrade url! SvcResponse was null?!?!?");
                    MainWindow.ShowError("Could not set url!", "Is the monitor service running?");
                } else if (r.Code != 0) {
                    logger.Error(r?.Error);
                    MainWindow.ShowError("Could not set url", r?.Error);
                } else {
                    this.OnShowBlurb?.Invoke("Config Saved.");
                }
            } catch (MonitorServiceException) {
                MainWindow.ShowError("Could Not Set URL", "The monitor service is offline");
            } catch (Exception ex) {
                logger.Error("unexpected error when setting automatic upgrade enabled", ex);
            }
        }

        private void UpdateUrl_TextChanged(object sender, TextChangedEventArgs e) {
            logger.Info("url: {}", state.AutomaticUpdateURL);
        }

        private void Button_Click(object sender, RoutedEventArgs e) {
            logger.Info("sender name: {}", sender);

        }
    }
}