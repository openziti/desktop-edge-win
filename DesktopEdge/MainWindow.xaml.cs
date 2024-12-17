//#define DEBUG_DUMP
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
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.IO;
using System.ServiceProcess;
using System.Linq;
using System.Diagnostics;
using System.Windows.Controls;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using System.Web;
using Microsoft.Toolkit.Uwp.Notifications;

using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;
using ZitiDesktopEdge.Utility;

using NLog;
using NLog.Config;
using NLog.Targets;
using Microsoft.Win32;

using Ziti.Desktop.Edge.Models;
using Ziti.Desktop.Edge;
using System.Text;
using Newtonsoft.Json;
using System.ComponentModel;
using static ZitiDesktopEdge.CommonDelegates;
using Ziti.Desktop.Edge.Utils;

namespace ZitiDesktopEdge {

    public partial class MainWindow : Window {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string RECOVER = "RECOVER";
        public System.Windows.Forms.NotifyIcon notifyIcon;
        public string Position = "Bottom";
        private DateTime _startDate;
        private System.Windows.Forms.Timer _tunnelUptimeTimer;
        private DataClient serviceClient = null;
        MonitorClient monitorClient = null;
        private bool _isAttached = true;
        private bool _isServiceInError = false;
        private int _right = 75;
        private int _left = 75;
        private int _top = 30;
        private int defaultHeight = 560;
        public int NotificationsShownCount = 0;
        private double _maxHeight = 800d;
        public string CurrentIcon = "white";
        private string[] suffixes = { "Bps", "kBps", "mBps", "gBps", "tBps", "pBps" };
        private string _blurbUrl = "";

        private DateTime NextNotificationTime;
        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        static System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();

        public static string ThisAssemblyName;
        public static string ExecutionDirectory;
        public static string ExpectedLogPathRoot;
        public static string ExpectedLogPathUI;
        public static string ExpectedLogPathServices;

        private static ZDEWViewState state;

        public static UIElement MouseDownControl;
        // Global MouseDown for all controls inside the window
        private void Window_GlobalMouseDown(object sender, MouseButtonEventArgs e) {
            Console.WriteLine("MOUSE DOWN ON: " + e.OriginalSource);
            MouseDownControl = e.OriginalSource as UIElement;
        }

        static MainWindow() {
            asm = System.Reflection.Assembly.GetExecutingAssembly();
            ThisAssemblyName = asm.GetName().Name;
            state = (ZDEWViewState)Application.Current.Properties["ZDEWViewState"];
#if DEBUG
            ExecutionDirectory = @"C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge";
#else
			ExecutionDirectory = Path.GetDirectoryName(asm.Location);
#endif
            ExpectedLogPathRoot = Path.Combine(ExecutionDirectory, "logs");
            ExpectedLogPathUI = Path.Combine(ExpectedLogPathRoot, "UI", $"{ThisAssemblyName}.log");
            ExpectedLogPathServices = Path.Combine(ExpectedLogPathRoot, "service", $"ziti-tunneler.log");
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e) {
            LoadIdentities(true);
        }

        private List<ZitiIdentity> identities {
            get {
                return (List<ZitiIdentity>)Application.Current.Properties["Identities"];
            }
        }

        /// <summary>
        /// The MFA Toggle was toggled
        /// </summary>
        /// <param name="isOn">True if the toggle was on</param>
        private async void MFAToggled(bool isOn) {
            if (isOn) {
                ShowLoad("Generating MFA", "MFA Setup Commencing, please wait");

                await serviceClient.EnableMFA(this.IdentityMenu.Identity.Identifier);
            } else {
                this.ShowMFA(IdentityMenu.Identity, 3);
            }

            HideLoad();
        }

        /// <summary>
        /// When a Service Client is ready to setup the MFA Authorization
        /// </summary>
        /// <param name="sender">The service client</param>
        /// <param name="e">The MFA Event</param>
        private void ServiceClient_OnMfaEvent(object sender, MfaEvent mfa) {
            HideLoad();
            this.Dispatcher.Invoke(async () => {
                if (mfa.Action == "enrollment_challenge") {
                    string url = HttpUtility.UrlDecode(mfa.ProvisioningUrl);
                    string secret = HttpUtility.ParseQueryString(url)["secret"];
                    this.IdentityMenu.Identity.RecoveryCodes = mfa?.RecoveryCodes?.ToArray();
                    SetupMFA(this.IdentityMenu.Identity, url, secret);
                } else if (mfa.Action == "auth_challenge") {
                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == mfa.Identifier) {
                            identities[i].WasNotified = false;
                            identities[i].WasFullNotified = false;
                            identities[i].IsMFANeeded = true;
                            identities[i].IsTimingOut = false;
                            break;
                        }
                    }
                } else if (mfa.Action == "enrollment_verification") {
                    if (mfa.Successful) {
                        var found = identities.Find(id => id.Identifier == mfa.Identifier);
                        for (int i = 0; i < identities.Count; i++) {
                            if (identities[i].Identifier == mfa.Identifier) {
                                identities[i].WasNotified = false;
                                identities[i].WasFullNotified = false;
                                identities[i].IsMFANeeded = false;
                                identities[i].IsMFAEnabled = true;
                                identities[i].IsTimingOut = false;
                                identities[i].LastUpdatedTime = DateTime.Now;
                                for (int j = 0; j < identities[i].Services.Count; j++) {
                                    identities[i].Services[j].TimeUpdated = DateTime.Now;
                                    identities[i].Services[j].TimeoutRemaining = identities[i].Services[j].Timeout;
                                }
                                found = identities[i];
                                found.IsMFAEnabled = true;
                                break;
                            }
                        }
                        if (this.IdentityMenu.Identity != null && this.IdentityMenu.Identity.Identifier == mfa.Identifier) this.IdentityMenu.Identity = found;
                        ShowMFARecoveryCodes(found);
                    } else {
                        await ShowBlurbAsync("Provided code could not be verified", "");
                    }
                } else if (mfa.Action == "enrollment_remove") {
                    if (mfa.Successful) {
                        var found = identities.Find(id => id.Identifier == mfa.Identifier);
                        for (int i = 0; i < identities.Count; i++) {
                            if (identities[i].Identifier == mfa.Identifier) {
                                identities[i].WasNotified = false;
                                identities[i].WasFullNotified = false;
                                identities[i].IsMFAEnabled = false;
                                identities[i].IsMFANeeded = false;
                                identities[i].LastUpdatedTime = DateTime.Now;
                                identities[i].IsTimingOut = false;
                                for (int j = 0; j < identities[i].Services.Count; j++) {
                                    identities[i].Services[j].TimeUpdated = DateTime.Now;
                                    identities[i].Services[j].TimeoutRemaining = 0;
                                }
                                found = identities[i];
                                break;
                            }
                        }
                        if (this.IdentityMenu.Identity != null && this.IdentityMenu.Identity.Identifier == mfa.Identifier) this.IdentityMenu.Identity = found;
                        await ShowBlurbAsync("MFA Disabled, Service Access Can Be Limited", "");
                    } else {
                        await ShowBlurbAsync("MFA Removal Failed", "");
                    }
                } else if (mfa.Action == "mfa_auth_status") {
                    var found = identities.Find(id => id.Identifier == mfa.Identifier);
                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == mfa.Identifier) {
                            identities[i].WasNotified = false;
                            identities[i].WasFullNotified = false;
                            identities[i].IsTimingOut = false;
                            identities[i].IsMFANeeded = !mfa.Successful;
                            identities[i].LastUpdatedTime = DateTime.Now;
                            for (int j = 0; j < identities[i].Services.Count; j++) {
                                identities[i].Services[j].TimeUpdated = DateTime.Now;
                                identities[i].Services[j].TimeoutRemaining = identities[i].Services[j].Timeout;
                            }
                            found = identities[i];
                            break;
                        }
                    }
                    if (this.IdentityMenu.Identity != null && this.IdentityMenu.Identity.Identifier == mfa.Identifier) this.IdentityMenu.Identity = found;
                    // ShowBlurb("mfa authenticated: " + mfa.Successful, "");
                } else {
                    await ShowBlurbAsync("Unexpected error when processing MFA", "");
                    logger.Error("unexpected action: " + mfa.Action);
                }

                LoadIdentities(true);
            });
        }

        /// <summary>
        /// Show the MFA Setup Modal
        /// </summary>
        /// <param name="identity">The Ziti Identity to Setup</param>
        public void SetupMFA(ZitiIdentity identity, string url, string secret) {
            MFASetup.Opacity = 0;
            MFASetup.Visibility = Visibility.Visible;
            MFASetup.Margin = new Thickness(0, 0, 0, 0);
            MFASetup.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(.3)));
            MFASetup.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));
            MFASetup.ShowSetup(identity, url, secret);
            ShowModal();
        }

        /// <summary>
        /// Show the MFA Authentication Screen when it is time to authenticate
        /// </summary>
        /// <param name="identity">The Ziti Identity to Authenticate</param>
        public void MFAAuthenticate(ZitiIdentity identity) {
            this.ShowMFA(identity, 1);
        }

        /// <summary>
        /// Show MFA for the identity and set the type of screen to show
        /// </summary>
        /// <param name="identity">The Identity that is currently active</param>
        /// <param name="type">The type of screen to show - 1 Setup, 2 Authenticate, 3 Remove MFA, 4 Regenerate Codes</param>
        private void ShowMFA(ZitiIdentity identity, int type) {
            MFASetup.Opacity = 0;
            MFASetup.Visibility = Visibility.Visible;
            MFASetup.Margin = new Thickness(0, 0, 0, 0);

            DoubleAnimation animatin = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
            animatin.Completed += Animatin_Completed;
            MFASetup.BeginAnimation(Grid.OpacityProperty, animatin);
            MFASetup.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

            MFASetup.ShowMFA(identity, type);

            ShowModal();
        }

        private void ShowJoinByUrl() {
            AddIdentityByURL.Opacity = 0;
            AddIdentityByURL.Visibility = Visibility.Visible;
            AddIdentityByURL.Margin = new Thickness(0, 0, 0, 0);

            DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
            AddIdentityByURL.BeginAnimation(Grid.OpacityProperty, animation);
            AddIdentityByURL.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

            ShowModal();
        }
        
        private void ShowJoinWith3rdPartyCA() {
            AddIdentityBy3rdPartyCA.Opacity = 0;
            AddIdentityBy3rdPartyCA.Visibility = Visibility.Visible;
            AddIdentityBy3rdPartyCA.Margin = new Thickness(0, 0, 0, 0);

            DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
            AddIdentityBy3rdPartyCA.BeginAnimation(Grid.OpacityProperty, animation);
            AddIdentityBy3rdPartyCA.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

            ShowModal();
        }

        private void Animatin_Completed(object sender, EventArgs e) {
            MFASetup.AuthCode.Focusable = true;
            MFASetup.AuthCode.Focus();
        }

        /// <summary>
        /// Show the MFA Recovery Codes
        /// </summary>
        /// <param name="identity">The Ziti Identity to Authenticate</param>
        async public void ShowMFARecoveryCodes(ZitiIdentity identity) {
            if (identity.IsMFAEnabled) {
                if (identity.IsMFAEnabled && identity.RecoveryCodes != null) {
                    MFASetup.Opacity = 0;
                    MFASetup.Visibility = Visibility.Visible;
                    MFASetup.Margin = new Thickness(0, 0, 0, 0);
                    MFASetup.BeginAnimation(Grid.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(.3)));
                    MFASetup.BeginAnimation(Grid.MarginProperty, new ThicknessAnimation(new Thickness(30, 30, 30, 30), TimeSpan.FromSeconds(.3)));

                    MFASetup.ShowRecovery(identity.RecoveryCodes, identity);

                    ShowModal();
                } else {
                    this.ShowMFA(IdentityMenu.Identity, 2);
                }
            } else {
                await ShowBlurbAsync("MFA is not setup on this Identity", "");
            }
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
        private void CloseJoinByUrl(bool isComplete, UserControl sender) {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
            animation.Completed += (s, e) => {
                sender.Visibility = Visibility.Collapsed;
            };
            sender.BeginAnimation(Grid.OpacityProperty, animation);
            sender.BeginAnimation(Grid.MarginProperty, animateThick);
            HideModal();
        }

        /// <summary>
        /// Close the MFA Screen with animation
        /// </summary>
        private void DoClose(bool isComplete, UserControl sender) {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
            animation.Completed += (s, e) => {
                sender.Visibility = Visibility.Collapsed;
            };
            sender.BeginAnimation(Grid.OpacityProperty, animation);
            sender.BeginAnimation(Grid.MarginProperty, animateThick);
            HideModal();
            if (isComplete) {
                if (MFASetup.Type == 1) {
                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == MFASetup.Identity.Identifier) {
                            identities[i] = MFASetup.Identity;
                            identities[i].LastUpdatedTime = DateTime.Now;
                        }
                    }
                }
            }
            if (IdentityMenu.IsVisible) {
                if (isComplete) {
                    if (MFASetup.Type == 2) {
                        ShowRecovery(IdentityMenu.Identity);
                    } else if (MFASetup.Type == 3) {
                    } else if (MFASetup.Type == 4) {
                        ShowRecovery(IdentityMenu.Identity);
                    }
                }
                IdentityMenu.UpdateView();
            }
            LoadIdentities(true);
        }

        private void AddIdentity(ZitiIdentity id) {
            semaphoreSlim.Wait();
            if (!identities.Any(i => id.Identifier == i.Identifier)) {
                identities.Add(id);
            }
            semaphoreSlim.Release();
        }

        private System.Windows.Forms.ContextMenu contextMenu;
        private System.Windows.Forms.MenuItem contextMenuItem;
        private System.ComponentModel.IContainer components;
        private MainViewModel props = null;
        public MainWindow() {
            InitializeComponent();

            props = new MainViewModel();
            DataContext = props;

            NextNotificationTime = DateTime.Now;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            string nlogFile = Path.Combine(ExecutionDirectory, ThisAssemblyName + "-log.config");

            ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;

            bool byFile = false;
            if (File.Exists(nlogFile)) {
                LogManager.Configuration = new XmlLoggingConfiguration(nlogFile);
                byFile = true;
            } else {
                var config = new LoggingConfiguration();
                // Targets where to log to: File and Console
                var logfile = new FileTarget("logfile") {
                    FileName = ExpectedLogPathUI,
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Rolling,
                    MaxArchiveFiles = 7,
                    AutoFlush = true,
                    Layout = "[${date:format=yyyy-MM-ddTHH:mm:ss.fff}Z] ${level:uppercase=true:padding=5}\t${logger}\t${message}\t${exception:format=tostring}",
                };
                var logconsole = new ConsoleTarget("logconsole");

                // Rules for mapping loggers to targets
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

                // Apply config
                LogManager.Configuration = config;
            }
            logger.Info("============================== UI started ==============================");
            logger.Info("logger initialized");
            logger.Info("    - version   : {0}", asm.GetName().Version.ToString());
            logger.Info("    - using file: {0}", byFile);
            logger.Info("    -       file: {0}", nlogFile);
            logger.Info("========================================================================");

            App.Current.MainWindow.WindowState = WindowState.Normal;
            App.Current.MainWindow.Deactivated += MainWindow_Deactivated;
            App.Current.MainWindow.Activated += MainWindow_Activated;
            App.Current.Exit += Current_Exit;
            App.Current.SessionEnding += Current_SessionEnding;


            this.components = new System.ComponentModel.Container();
            this.contextMenu = new System.Windows.Forms.ContextMenu();
            this.contextMenuItem = new System.Windows.Forms.MenuItem();
            this.contextMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] { this.contextMenuItem });

            this.contextMenuItem.Index = 0;
            this.contextMenuItem.Text = "&Close UI";
            this.contextMenuItem.Click += new System.EventHandler(this.contextMenuItem_Click);


            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Visible = true;
            notifyIcon.Click += TargetNotifyIcon_Click;
            notifyIcon.Visible = true;
            notifyIcon.BalloonTipClosed += NotifyIcon_BalloonTipClosed;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            notifyIcon.ContextMenu = this.contextMenu;

            IdentityMenu.OnDetach += HandleDetached;
            IdentityMenu.OnAttach += HandleAttach;
            MainMenu.OnDetach += HandleDetached;

            this.MainMenu.MainWindow = this;
            this.IdentityMenu.MainWindow = this;
            SetNotifyIcon("white");

            this.PreviewKeyDown += KeyPressed;
            MFASetup.OnLoad += MFASetup_OnLoad;
            MFASetup.OnError += MFASetup_OnError;
        }

        async private void MFASetup_OnError(string message) {
            await ShowBlurbAsync(message, "", "error");
        }

        private static ToastButton feedbackToastButton = new ToastButton()
                        .SetContent("Click here to collect logs")
                        .AddArgument("action", "feedback");

        private void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat e) {
            this.Dispatcher.Invoke(() => {
                if (e.Argument != null && e.Argument.Length > 0) {
                    string[] items = e.Argument.Split(';');
                    if (items.Length > 0) {
                        string[] values = items[0].Split('=');
                        if (values.Length == 2) {
                            string identifier = values[1];
                            for (int i = 0; i < identities.Count; i++) {
                                if (identities[i].Identifier == identifier) {
                                    ShowMFA(identities[i], 1);
                                    break;
                                }
                            }
                        }
                    }
                }

                ToastArguments args = ToastArguments.Parse(e.Argument);
                string value = null;
                if (args.TryGetValue("action", out value)) {
                    this.Dispatcher.Invoke(() => {
                        MainMenu.CollectFeedbackLogs(e, null);
                    });
                }
                this.Show();
                this.Activate();
            });
        }

        private void KeyPressed(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                if (IdentityMenu.Visibility == Visibility.Visible) IdentityMenu.Visibility = Visibility.Collapsed;
                else if (MainMenu.Visibility == Visibility.Visible) MainMenu.Visibility = Visibility.Collapsed;
            }
        }

        private void MFASetup_OnLoad(bool isComplete, string title, string message) {
            if (isComplete) HideLoad();
            else ShowLoad(title, message);
        }

        private void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e) {
            if (notifyIcon != null) {
                notifyIcon.Visible = false;
                notifyIcon.Icon.Dispose();
                notifyIcon.Dispose();
                notifyIcon = null;
            }
            Application.Current.Shutdown();
        }

        private void Current_Exit(object sender, ExitEventArgs e) {
            if (notifyIcon != null) {
                notifyIcon.Visible = false;
                if (notifyIcon.Icon != null) {
                    notifyIcon.Icon.Dispose();
                }
                notifyIcon.Dispose();
                notifyIcon = null;
            }
        }

        private void contextMenuItem_Click(object Sender, EventArgs e) {
            Application.Current.Shutdown();
        }

        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e) {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) {
                System.Windows.Forms.MouseEventArgs mea = (System.Windows.Forms.MouseEventArgs)e;
                this.Show();
                this.Activate();
                //Do the awesome left clickness
            } else if (e.Button == System.Windows.Forms.MouseButtons.Right) {
                //Do the wickedy right clickness
            } else {
                //Some other button from the enum :)
            }
        }

        private void NotifyIcon_BalloonTipClosed(object sender, EventArgs e) {
            var thisIcon = (System.Windows.Forms.NotifyIcon)sender;
            thisIcon.Visible = false;
            thisIcon.Dispose();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
            HandleDetached(e);
        }

        private void HandleAttach(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Right) {
                _isAttached = true;
                IdentityMenu.Arrow.Visibility = Visibility.Visible;
                Arrow.Visibility = Visibility.Visible;
                MainMenu.Retach();
            }
        }

        private void HandleDetached(MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left) {
                _isAttached = false;
                IdentityMenu.Arrow.Visibility = Visibility.Collapsed;
                Arrow.Visibility = Visibility.Collapsed;
                MainMenu.Detach();
                this.DragMove();
            }
        }

        private void MainWindow_Activated(object sender, EventArgs e) {
            Placement();
            this.Show();
            this.Visibility = Visibility.Visible;
            this.Opacity = 1;
        }

        private void MainWindow_Deactivated(object sender, EventArgs e) {
            if (this._isAttached) {
                this.Visibility = Visibility.Hidden;
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            if (notifyIcon != null) {
                notifyIcon.Visible = false;
                notifyIcon.Icon.Dispose();
                notifyIcon.Dispose();
                notifyIcon = null;
            }
            Application.Current.Shutdown();
        }

        private void SetCantDisplay(string title, string detailMessage, Visibility closeButtonVisibility) {
            this.Dispatcher.Invoke(() => {
                NoServiceView.Visibility = Visibility.Visible;
                CloseErrorButton.IsEnabled = true;
                CloseErrorButton.Visibility = closeButtonVisibility;
                ErrorMsg.Content = title;
                ErrorMsgDetail.Content = detailMessage;
                SetNotifyIcon("red");
                _isServiceInError = true;
                UpdateServiceView();
            });
        }

        private void TargetNotifyIcon_Click(object sender, EventArgs e) {
            this.Show();
            this.Activate();
            Application.Current.MainWindow.Activate();
        }

        private void UpdateServiceView() {
            if (_isServiceInError) {
                AddIdAreaButton.Opacity = 0.1;
                AddIdAreaButton.IsEnabled = false;
                AddIdButton.Opacity = 0.1;
                AddIdButton.IsEnabled = false;
                ConnectButton.Opacity = 0.1;
                StatArea.Opacity = 0.1;
            } else {
                AddIdAreaButton.Opacity = 1.0;
                AddIdAreaButton.IsEnabled = true;
                AddIdButton.Opacity = 1.0;
                AddIdButton.IsEnabled = true;
                StatArea.Opacity = 1.0;
                ConnectButton.Opacity = 1.0;
            }
            TunnelConnected(!_isServiceInError);
        }

        private void App_ReceiveString(string obj) {
            Console.WriteLine(obj);
            this.Show();
            this.Activate();
        }

        async private void MainWindow_Loaded(object sender, RoutedEventArgs e) {

            Window window = Window.GetWindow(App.Current.MainWindow);
            ZitiDesktopEdge.App app = (ZitiDesktopEdge.App)App.Current;
            app.ReceiveString += App_ReceiveString;

            // add a new service client
            serviceClient = new DataClient("UI-DataClient");
            serviceClient.OnClientConnected += ServiceClient_OnClientConnected;
            serviceClient.OnClientDisconnected += ServiceClient_OnClientDisconnected;
            serviceClient.OnIdentityEvent += ServiceClient_OnIdentityEvent;
            serviceClient.OnMetricsEvent += ServiceClient_OnMetricsEvent;
            serviceClient.OnServiceEvent += ServiceClient_OnServiceEvent;
            serviceClient.OnTunnelStatusEvent += ServiceClient_OnTunnelStatusEvent;
            serviceClient.OnMfaEvent += ServiceClient_OnMfaEvent;
            serviceClient.OnLogLevelEvent += ServiceClient_OnLogLevelEvent;
            serviceClient.OnBulkServiceEvent += ServiceClient_OnBulkServiceEvent;
            serviceClient.OnNotificationEvent += ServiceClient_OnNotificationEvent;
            serviceClient.OnControllerEvent += ServiceClient_OnControllerEvent;
            serviceClient.OnAuthenticationEvent += ServiceClient_OnAuthenticationEvent;
            serviceClient.OnCommunicationError += ServiceClient_OnCommunicationError;
            Application.Current.Properties.Add("ServiceClient", serviceClient);

            monitorClient = new MonitorClient("UI-MonitorClient");
            monitorClient.OnClientConnected += MonitorClient_OnClientConnected;
            monitorClient.OnNotificationEvent += MonitorClient_OnInstallationNotificationEvent;
            monitorClient.OnServiceStatusEvent += MonitorClient_OnServiceStatusEvent;
            monitorClient.OnShutdownEvent += MonitorClient_OnShutdownEvent;
            monitorClient.OnCommunicationError += MonitorClient_OnCommunicationError;
            monitorClient.OnReconnectFailure += MonitorClient_OnReconnectFailure;
            Application.Current.Properties.Add("MonitorClient", monitorClient);

            Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
            MainMenu.OnAttachmentChange += AttachmentChanged;
            MainMenu.OnLogLevelChanged += LogLevelChanged;
            MainMenu.OnShowBlurb += MainMenu_OnShowBlurb;
            IdentityMenu.OnError += IdentityMenu_OnError;

            try {
                await serviceClient.ConnectAsync();
                await serviceClient.WaitForConnectionAsync();
            } catch /*ignored for now (Exception ex) */
              {
                ShowServiceNotStarted();
                serviceClient.Reconnect();
            }

            try {
                await monitorClient.ConnectAsync();
                await monitorClient.WaitForConnectionAsync();
            } catch /*ignored for now (Exception ex) */
              {
                monitorClient.Reconnect();
            }

            IdentityMenu.OnForgot += IdentityForgotten;
            Placement();
        }

        private async void ServiceClient_OnAuthenticationEvent(object sender, AuthenticationEvent e) {
            ZitiIdentity found = identities.Find(i => i.Identifier == e.Identifier);
            if(found != null) {
                if (e.Action == "error") {
                    found.AuthInProgress = false;
                    await Dispatcher.BeginInvoke(new Action(async () => {
                        await ShowBlurbAsync("Authentication Failed", "External Auth Failed");
                    }));
                }
            }
        }

        private void ServiceClient_OnCommunicationError(object sender, Exception e) {
            serviceClient.Reconnect();
            string msg = "Operation Timed Out";
            ShowError(msg, e.Message);
        }

        private void MonitorClient_OnCommunicationError(object sender, Exception e) {
            string msg = "Communication Error with monitor?";
            ShowError(msg, e.Message);
        }

        private void MainMenu_OnShowBlurb(string message) {
            _ = ShowBlurbAsync(message, "", "info");
        }

        private void ServiceClient_OnBulkServiceEvent(object sender, BulkServiceEvent e) {
            var found = identities.Find(id => id.Identifier == e.Identifier);
            if (found == null) {
                logger.Warn($"{e.Action} service event for {e.Identifier} but the provided identity identifier was not found!");
                return;
            } else {
                if (e.RemovedServices != null) {
                    foreach (var removed in e.RemovedServices) {
                        removeService(found, removed);
                    }
                }
                if (e.AddedServices != null) {
                    foreach (var added in e.AddedServices) {
                        addService(found, added);
                    }
                }
                LoadIdentities(true);
                this.Dispatcher.Invoke(() => {
                    IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                    if (deets.IsVisible) {
                        deets.UpdateView();
                    }
                });
            }
        }

        private void ServiceClient_OnNotificationEvent(object sender, NotificationEvent e) {
            var displayMFARequired = false;
            var displayMFATimeout = false;
            foreach (var notification in e.Notification) {
                var found = identities.Find(id => id.Identifier == notification.Identifier);
                if (found == null) {
                    logger.Warn($"{e.Op} event for {notification.Identifier} but the provided identity identifier was not found!");
                    continue;
                } else {
                    found.TimeoutMessage = notification.Message;
                    found.MaxTimeout = notification.MfaMaximumTimeout;
                    found.MinTimeout = notification.MfaMinimumTimeout;

                    if (notification.MfaMinimumTimeout == 0) {
                        // display mfa token icon
                        displayMFARequired = true;
                    } else {
                        displayMFATimeout = true;
                    }

                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == found.Identifier) {
                            identities[i] = found;
                            break;
                        }
                    }
                }
            }

            // we may need to display mfa icon, based on the timer in UI, remove found.MFAInfo.ShowMFA setting in this function. 
            // the below function can show mfa icon even after user authenticates successfully, in race conditions
            if (displayMFARequired || displayMFATimeout) {
                this.Dispatcher.Invoke(() => {
                    IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                    if (deets.IsVisible) {
                        deets.UpdateView();
                    }
                });
            }
            LoadIdentities(true);
        }

        private void ServiceClient_OnControllerEvent(object sender, ControllerEvent e) {
            logger.Debug($"==== ControllerEvent    : action:{e.Action} identifier:{e.Identifier}");
            // commenting this block, because when it receives the disconnected events, identities are disabled and
            // it is not allowing me to click/perform any operation on the identity
            // the color of the title is also too dark, and it is not clearly visible, when the identity is disconnected 
            /* if (e.Action == "connected") {
				var found = identities.Find(i => i.Identifier == e.Identifier);
				found.IsConnected = true;
				for (int i = 0; i < identities.Count; i++) {
					if (identities[i].Identifier == found.Identifier) {
						identities[i] = found;
						break;
					}
				}
				LoadIdentities(true);
			} else if (e.Action == "disconnected") {
				var found = identities.Find(i => i.Identifier == e.Identifier);
				found.IsConnected = false;
				for (int i = 0; i < identities.Count; i++) {
					if (identities[i].Identifier == found.Identifier) {
						identities[i] = found;
						break;
					}
				}
				LoadIdentities(true);
			} */
        }


        string nextVersionStr = null;
        private void MonitorClient_OnReconnectFailure(object sender, object e) {
            logger.Trace("OnReconnectFailure triggered");
            if (nextVersionStr == null) {
                // check for the current version
                nextVersionStr = "checking for update";
                Version nextVersion = GithubAPI.GetVersion(GithubAPI.GetJson(GithubAPI.ProdUrl));
                nextVersionStr = nextVersion.ToString();
                Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; //fetch from ziti?

                int compare = currentVersion.CompareTo(nextVersion);
                if (compare < 0) {
                    MainMenu.SetAppUpgradeAvailableText("Upgrade available: " + nextVersionStr);
                    logger.Info("upgrade is available. Published version: {} is newer than the current version: {}", nextVersion, currentVersion);
                    //UpgradeAvailable();
                } else if (compare > 0) {
                    logger.Info("the version installed: {0} is newer than the released version: {1}", currentVersion, nextVersion);
                    MainMenu.SetAppIsNewer("This version is newer than the latest: " + nextVersionStr);
                } else {
                    logger.Info("Current version installed: {0} is the same as the latest released version {1}", currentVersion, nextVersion);
                    MainMenu.SetAppUpgradeAvailableText("");
                }
            }
        }

        private void MonitorClient_OnShutdownEvent(object sender, StatusEvent e) {
            logger.Info("The monitor has indicated the application should shut down.");
            this.Dispatcher.Invoke(() => {
                Application.Current.Shutdown();
            });
        }

        private void MonitorClient_OnServiceStatusEvent(object sender, MonitorServiceStatusEvent evt) {
            this.Dispatcher.Invoke(() => {
                try {
                    if (evt.Message?.ToLower() == "upgrading") {
                        logger.Info("The monitor has indicated an upgrade is in progress. Shutting down the UI");
                        UpgradeSentinel.StartUpgradeSentinel();

                        App.Current.Exit -= Current_Exit;
                        logger.Info("Removed Current_Exit handler");
                        notifyIcon.Visible = false;
                        notifyIcon.Icon.Dispose();
                        notifyIcon.Dispose();
                        Application.Current.Shutdown();
                        return;
                    }
                    SetAutomaticUpdateEnabled(evt.AutomaticUpgradeDisabled, evt.AutomaticUpgradeURL);
                    if (evt.Code != 0) {
                        logger.Error("CODE: " + evt.Code);
                        if (MainMenu.ShowUnexpectedFailure) {
                            ShowToast("The data channel has stopped unexpectedly", $"If this keeps happening please collect logs and report the issue.", feedbackToastButton);
                        }
                    }
                    MainMenu.ShowUpdateAvailable();
                    logger.Debug("MonitorClient_OnServiceStatusEvent: {0}", evt.Status);
                    Application.Current.Properties["ReleaseStream"] = evt.ReleaseStream;

                    ServiceControllerStatus status = (ServiceControllerStatus)Enum.Parse(typeof(ServiceControllerStatus), evt.Status);

                    switch (status) {
                        case ServiceControllerStatus.Running:
                            logger.Info("Service is started");
                            break;
                        case ServiceControllerStatus.Stopped:
                            logger.Info("Service is stopped");
                            ShowServiceNotStarted();
                            break;
                        case ServiceControllerStatus.StopPending:
                            logger.Info("Service is stopping...");

                            this.Dispatcher.Invoke(async () => {
                                SetCantDisplay("The Service is Stopping", "Please wait while the service stops", Visibility.Visible);
                                await WaitForServiceToStop(DateTime.Now + TimeSpan.FromSeconds(30));
                            });
                            break;
                        case ServiceControllerStatus.StartPending:
                            logger.Info("Service is starting...");
                            break;
                        case ServiceControllerStatus.PausePending:
                            logger.Warn("UNEXPECTED STATUS: PausePending");
                            break;
                        case ServiceControllerStatus.Paused:
                            logger.Warn("UNEXPECTED STATUS: Paused");
                            break;
                        default:
                            logger.Warn("UNEXPECTED STATUS: {0}", evt.Status);
                            break;
                    }
                } catch (Exception ex) {
                    logger.Warn(ex, "unexpected exception in MonitorClient_OnServiceStatusEvent? {0}", ex.Message);
                }
            });
        }

        private void SetAutomaticUpdateEnabled(string enabled, string url) {
            state.AutomaticUpdatesDisabled = bool.Parse(enabled);
            state.AutomaticUpdateURL = url;
        }

        private void MonitorClient_OnInstallationNotificationEvent(object sender, InstallationNotificationEvent evt) {
            this.Dispatcher.Invoke(() => {
                logger.Debug("MonitorClient_OnInstallationNotificationEvent: {0}", evt.Message);
                switch (evt.Message?.ToLower()) {
                    case "installationupdate":
                        logger.Debug("Installation Update is available - {0}", evt.ZDEVersion);
                        var remaining = evt.InstallTime - DateTime.Now;

                        state.PendingUpdate.Version = evt.ZDEVersion;
                        state.PendingUpdate.InstallTime = evt.InstallTime;
                        state.UpdateAvailable = true;
                        SetAutomaticUpdateEnabled(evt.AutomaticUpgradeDisabled, evt.AutomaticUpgradeURL);
                        MainMenu.ShowUpdateAvailable();
                        AlertCanvas.Visibility = Visibility.Visible;

                        if (isToastEnabled()) {
                            if (!state.AutomaticUpdatesDisabled) {
                                if (remaining.TotalSeconds < 60) {
                                    //this is an immediate update - show a different message
                                    ShowToast("Ziti Desktop Edge will initiate auto installation in the next minute!");
                                } else {
                                    if (DateTime.Now > NextNotificationTime) {
                                        ShowToast($"Update {evt.ZDEVersion} is available for Ziti Desktop Edge and will be automatically installed by " + evt.InstallTime);
                                        NextNotificationTime = DateTime.Now + evt.NotificationDuration;
                                    } else {
                                        logger.Debug("Skipping notification. Time until next notification {} seconds which is at {}", (int)((NextNotificationTime - DateTime.Now).TotalSeconds), NextNotificationTime);
                                    }
                                }
                            } else {
                                ShowToast("New version available", $"Version {evt.ZDEVersion} is available for Ziti Desktop Edge", null);
                            }
                            SetNotifyIcon("");
                            // display a tag in UI and a button for the update software
                        }
                        break;
                    case "configuration changed":
                        break;
                    default:
                        logger.Debug("unexpected event type?");
                        break;
                }
            });
        }

        private bool isToastEnabled() {
            bool result;
            //only show notifications once if automatic updates are disabled
            if (NotificationsShownCount == 0) {
                result = true; //regardless - if never notified, always return true
            } else {
                result = !state.AutomaticUpdatesDisabled;
            }
            return result;
        }

        private void ShowToast(string header, string message, ToastButton button) {
            try {
                logger.Debug("showing toast: {} {}", header, message);
                var builder = new ToastContentBuilder()
                    .AddArgument("notbutton", "click")
                    .AddText(header)
                    .AddText(message);
                if (button != null) {
                    builder.AddButton(button);
                }
                builder.Show();
                NotificationsShownCount++;
            } catch {
                logger.Warn("couldn't show toast: {} {}", header, message);
            }
        }


        private void ShowToast(string message) {
            ShowToast("Important Notice", message, null);
        }

        async private Task WaitForServiceToStop(DateTime until) {
            //continually poll for the service to stop. If it is stuck - ask the user if they want to try to force
            //close the service
            while (DateTime.Now < until) {
                await Task.Delay(250);
                MonitorServiceStatusEvent resp = await monitorClient.StatusAsync();
                if (resp.IsStopped()) {
                    // good - that's what we are waiting for...
                    return;
                } else {
                    // bad - not stopped yet...
                    logger.Debug("Waiting for service to stop... Still not stopped yet. Status: {0}", resp.Status);
                }
            }
            // real bad - means it's stuck probably. Ask the user if they want to try to force it...
            logger.Warn("Waiting for service to stop... Service did not reach stopped state in the expected amount of time.");
            SetCantDisplay("The Service Appears Stuck", "Would you like to try to force close the service?", Visibility.Visible);
            CloseErrorButton.Content = "Force Quit";
            CloseErrorButton.Click -= CloseError;
            CloseErrorButton.Click += ForceQuitButtonClick;
        }

        async private void ForceQuitButtonClick(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            MonitorServiceStatusEvent status = await monitorClient.ForceTerminateAsync();
            if (status.IsStopped()) {
                //good
                CloseErrorButton.Click += CloseError; //reset the close button...
                CloseErrorButton.Click -= ForceQuitButtonClick;
            } else {
                //bad...
                SetCantDisplay("The Service Is Still Running", "Current status is: " + status.Status, Visibility.Visible);
            }
        }

        async private void StartZitiService(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            try {
                ShowLoad("Starting", "Starting the data service");
                logger.Info("StartZitiService");
                var r = await monitorClient.StartServiceAsync(TimeSpan.FromSeconds(60));
                if (r.Code != 0) {
                    logger.Debug("ERROR: {0} : {1}", r.Message, r.Error);
                } else {
                    logger.Info("Service started!");
                    CloseErrorButton.Click -= StartZitiService;
                    CloseError(null, null);
                }
            } catch (MonitorServiceException me) {
                logger.Warn("the monitor service appears offline. {0}", me);
                CloseErrorButton.IsEnabled = true;
                HideLoad();
                ShowError("Error Starting Service", "The monitor service is offline");
            } catch (Exception ex) {
                logger.Error(ex, "UNEXPECTED ERROR!");
                CloseErrorButton.IsEnabled = true;
                HideLoad();
                ShowError("Unexpected Error", "Code 2:" + ex.Message);
            }
            CloseErrorButton.IsEnabled = true;
            // HideLoad();
        }

        private void ShowServiceNotStarted() {
            TunnelConnected(false);
            LoadIdentities(true);
        }

        private void MonitorClient_OnClientConnected(object sender, object e) {
            logger.Debug("MonitorClient_OnClientConnected");
            MainMenu.SetAppUpgradeAvailableText("");
        }

        async private Task<bool> LogLevelChanged(string level) {
            int logsSet = 0;
            try {
                await serviceClient.SetLogLevelAsync(level);
                logsSet++;
                await monitorClient.SetLogLevelAsync(level);
                logsSet++;
                Ziti.Desktop.Edge.Utils.UIUtils.SetLogLevel(level);
                return true;
            } catch (Exception ex) {
                logger.Error(ex, "Unexpected error. logsSet: {0}", logsSet);
                if (logsSet > 1) {
                    await ShowBlurbAsync("Unexpected error setting logs?", "");
                } else if (logsSet > 0) {
                    await ShowBlurbAsync("Failed to set monitor client log level", "");
                } else {
                    await ShowBlurbAsync("Failed to set log levels", "");
                }
            }
            return false;
        }

        private void IdentityMenu_OnError(string message) {
            ShowError("Identity Error", message);
        }

        private void ServiceClient_OnClientConnected(object sender, object e) {
            this.Dispatcher.Invoke(() => {
                MainMenu.Connected();
                NoServiceView.Visibility = Visibility.Collapsed;
                _isServiceInError = false;
                UpdateServiceView();
                SetNotifyIcon("white");
                LoadIdentities(true);
            });
        }

        private void ServiceClient_OnClientDisconnected(object sender, object e) {
            this.Dispatcher.Invoke(() => {
                AddIdAreaButton.IsEnabled = false;
                IdentityMenu.Visibility = Visibility.Collapsed;
                MFASetup.Visibility = Visibility.Collapsed;
                HideModal();
                MainMenu.Disconnected();
                for (int i = 0; i < IdList.Children.Count; i++) {
                    IdentityItem item = (IdentityItem)IdList.Children[i];
                    item.StopTimers();
                }
                IdList.Children.Clear();
                if (e != null) {
                    logger.Debug(e.ToString());
                }
                //SetCantDisplay("Start the Ziti Tunnel Service to continue");
                SetNotifyIcon("red");
                ShowServiceNotStarted();
            });
        }

        /// <summary>
        /// If an identity gets added late, execute this.
        /// 
        /// Do not update services for identity events
        /// </summary>
        /// <param name="sender">The sending service</param>
        /// <param name="e">The identity event</param>
        private void ServiceClient_OnIdentityEvent(object sender, IdentityEvent e) {
            if (e == null) return;

            ZitiIdentity zid = ZitiIdentity.FromClient(e.Id);
            logger.Debug($"==== IdentityEvent    : action:{e.Action} identifer:{e.Id.Identifier} name:{e.Id.Name} ");

            this.Dispatcher.Invoke(async () => {
                if (e.Action == "added" || e.Action == "needs_ext_login") {
                    var found = identities.Find(i => i.Identifier == e.Id.Identifier);
                    if (found == null) {
                        AddIdentity(zid);
                        LoadIdentities(true);
                    } else {
                        // means we are getting an update for some reason. compare the identities and use the latest info
                        // for external auth, this event will return after external auth. track if the auth is in progress or not
                        // and clear the flag here if it succeeds, else pop a 'auth failed'
                        if (found.AuthInProgress) {
                            found.AuthInProgress = false; //regardless clear it here
                            if (!zid.NeedsExtAuth) {
                            }
                            else {
                                // seems bad?
                                logger.Warn("Identity: {} AuthInProgress but still NeedsExtAuth?", found.Identifier);
                                //_ = ShowBlurbAsync("Authentication Failed2", "External Auth Failed");
                            }
                        }
                        if (zid.Name != null && zid.Name.Length > 0) found.Name = zid.Name;
                        if (zid.ControllerUrl != null && zid.ControllerUrl.Length > 0) found.ControllerUrl = zid.ControllerUrl;
                        if (zid.ContollerVersion != null && zid.ContollerVersion.Length > 0) found.ContollerVersion = zid.ContollerVersion;
                        found.IsEnabled = zid.IsEnabled;
                        found.IsMFAEnabled = e.Id.MfaEnabled;
                        found.IsConnected = true;
                        found.NeedsExtAuth = e.Id.NeedsExtAuth;
                        found.ExtAuthProviders = e.Id.ExtAuthProviders;
                        for (int i = 0; i < identities.Count; i++) {
                            if (identities[i].Identifier == found.Identifier) {
                                identities[i] = found;
                                break;
                            }
                        }
                        LoadIdentities(true);
                    }
                } else if (e.Action == "updated") {
                    //this indicates that all updates have been sent to the UI... wait for 2 seconds then trigger any ui updates needed
                    await Task.Delay(2000);
                    LoadIdentities(true);
                } else if (e.Action == "connected") {
                    var found = identities.Find(i => i.Identifier == e.Id.Identifier);
                    found.IsConnected = true;
                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == found.Identifier) {
                            identities[i] = found;
                            break;
                        }
                    }
                    LoadIdentities(true);
                } else if (e.Action == "disconnected") {
                    var found = identities.Find(i => i.Identifier == e.Id.Identifier);
                    found.IsConnected = false;
                    for (int i = 0; i < identities.Count; i++) {
                        if (identities[i].Identifier == found.Identifier) {
                            identities[i] = found;
                            break;
                        }
                    }
                    LoadIdentities(true);
                } else {
                    logger.Warn("unexpected action received: {}", e.Action);
                    IdentityForgotten(ZitiIdentity.FromClient(e.Id));
                }
            });
            logger.Debug($"IDENTITY EVENT. Action: {e.Action} identifier: {zid.Identifier}");
        }

        private void ServiceClient_OnMetricsEvent(object sender, List<Identity> ids) {
            if (ids != null) {
                long totalUp = 0;
                long totalDown = 0;
                foreach (var id in ids) {
                    //logger.Debug($"==== MetricsEvent : id {id.Name} down: {id.Metrics.Down} up:{id.Metrics.Up}");
                    if (id?.Metrics != null) {
                        totalDown += id.Metrics.Down;
                        totalUp += id.Metrics.Up;
                    }
                }
                this.Dispatcher.Invoke(() => {
                    SetSpeed(totalUp, UploadSpeed, UploadSpeedLabel);
                    SetSpeed(totalDown, DownloadSpeed, DownloadSpeedLabel);
                });
            }
        }

        public void SetSpeed(decimal bytes, Label speed, Label speedLabel) {
            int counter = 0;
            while (Math.Round(bytes / 1024) >= 1) {
                bytes = bytes / 1024;
                counter++;
            }
            speed.Content = bytes.ToString("0.0");
            speedLabel.Content = suffixes[counter];
        }

        private void ServiceClient_OnServiceEvent(object sender, ServiceEvent e) {
            if (e == null) return;

            logger.Debug($"==== ServiceEvent : action:{e.Action} identifier:{e.Identifier} name:{e.Service.Name} ");
            var found = identities.Find(id => id.Identifier == e.Identifier);
            if (found == null) {
                logger.Debug($"{e.Action} service event for {e.Service.Name} but the provided identity identifier {e.Identifier} is not found!");
                return;
            }

            if (e.Action == "added") {
                addService(found, e.Service);
            } else {
                removeService(found, e.Service);
            }
            LoadIdentities(true);
            this.Dispatcher.Invoke(() => {
                IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                if (deets.IsVisible) {
                    deets.UpdateView();
                }
            });
        }

        private void addService(ZitiIdentity found, Service added) {
            ZitiService zs = new ZitiService(added);
            var svc = found.Services.Find(s => s.Name == zs.Name);
            if (svc == null) {
                logger.Debug("Service Added: " + zs.Name);
                found.Services.Add(zs);
                if (zs.HasFailingPostureCheck()) {
                    found.HasServiceFailingPostureCheck = true;
                    if (zs.PostureChecks.Any(p => !p.IsPassing && p.QueryType == "MFA")) {
                        found.IsMFANeeded = true;
                    }
                }
            } else {
                logger.Debug("the service named " + zs.Name + " is already accounted for on this identity.");
            }
        }

        private void removeService(ZitiIdentity found, Service removed) {
            logger.Debug("removing the service named: {0}", removed.Name);
            found.Services.RemoveAll(s => s.Name == removed.Name);
        }

        private void ServiceClient_OnTunnelStatusEvent(object sender, TunnelStatusEvent e) {
            if (e == null) return; //just skip it for now...
            logger.Debug($"==== TunnelStatusEvent: ");
            Application.Current.Properties.Remove("CurrentTunnelStatus");
            Application.Current.Properties.Add("CurrentTunnelStatus", e.Status);
#if DEBUG && DEBUG_DUMP
            e.Status.Dump(Console.Out);
#endif
            this.Dispatcher.Invoke(() => {
                /*if (e.ApiVersion != DataClient.EXPECTED_API_VERSION) {
					SetCantDisplay("Version mismatch!", "The version of the Service is not compatible", Visibility.Visible);
					return;
				}*/
                this.MainMenu.LogLevel = e.Status.LogLevel;
                Ziti.Desktop.Edge.Utils.UIUtils.SetLogLevel(e.Status.LogLevel);
                InitializeTimer((int)e.Status.Duration);
                LoadStatusFromService(e.Status);
                LoadIdentities(true);
                IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
                if (deets.IsVisible) {
                    deets.UpdateView();
                }
            });
        }

        private void ServiceClient_OnLogLevelEvent(object sender, LogLevelEvent e) {
            if (e.LogLevel != null) {
                SetLogLevel_monitor(e.LogLevel);
                this.Dispatcher.Invoke(() => {
                    this.MainMenu.LogLevel = e.LogLevel;
                    Ziti.Desktop.Edge.Utils.UIUtils.SetLogLevel(e.LogLevel);
                });
            }
        }

        async private void SetLogLevel_monitor(string loglevel) {
            await monitorClient.SetLogLevelAsync(loglevel);
        }

        private void IdentityForgotten(ZitiIdentity forgotten) {
            ZitiIdentity idToRemove = null;
            foreach (var id in identities) {
                if (id.Identifier == forgotten.Identifier) {
                    idToRemove = id;
                    break;
                }
            }
            identities.Remove(idToRemove);
            LoadIdentities(false);
        }

        private void AttachmentChanged(bool attached) {
            _isAttached = attached;
            if (!_isAttached) {
                SetLocation();
            }
            Placement();
            MainMenu.Visibility = Visibility.Collapsed;
        }

        private void LoadStatusFromService(TunnelStatus status) {
            //clear any identities
            this.identities.Clear();

            if (status != null) {
                _isServiceInError = false;
                UpdateServiceView();
                NoServiceView.Visibility = Visibility.Collapsed;
                SetNotifyIcon("green");

                AddIdAreaButton.IsEnabled = true;
                if (!Application.Current.Properties.Contains("ip")) {
                    Application.Current.Properties.Add("ip", status?.IpInfo?.Ip);
                } else {
                    Application.Current.Properties["ip"] = status?.IpInfo?.Ip;
                }
                if (!Application.Current.Properties.Contains("subnet")) {
                    Application.Current.Properties.Add("subnet", status?.IpInfo?.Subnet);
                } else {
                    Application.Current.Properties["subnet"] = status?.IpInfo?.Subnet;
                }
                if (!Application.Current.Properties.Contains("mtu")) {
                    Application.Current.Properties.Add("mtu", status?.IpInfo?.MTU);
                } else {
                    Application.Current.Properties["mtu"] = status?.IpInfo?.MTU;
                }
                if (!Application.Current.Properties.Contains("dns")) {
                    Application.Current.Properties.Add("dns", status?.IpInfo?.DNS);
                } else {
                    Application.Current.Properties["dns"] = status?.IpInfo?.DNS;
                }
                if (!Application.Current.Properties.Contains("dnsenabled")) {
                    Application.Current.Properties.Add("dnsenabled", status?.AddDns);
                } else {
                    Application.Current.Properties["dnsenabled"] = status?.AddDns;
                }

                string key = "ApiPageSize";
                if (!Application.Current.Properties.Contains(key)) {
                    Application.Current.Properties.Add(key, status?.ApiPageSize);
                } else {
                    Application.Current.Properties[key] = status?.ApiPageSize;
                }

                foreach (var id in status.Identities) {
                    updateViewWithIdentity(id);
                }
                //LoadIdentities(true);
            } else {
                ShowServiceNotStarted();
            }
        }

        private void updateViewWithIdentity(Identity id) {
            var zid = ZitiIdentity.FromClient(id);
            foreach (var i in identities) {
                if (i.Identifier == zid.Identifier) {
                    identities.Remove(i);
                    break;
                }
            }
            identities.Add(zid);
        }

        private bool IsTimingOut() {
            if (identities != null) {
                for (int i = 0; i < identities.Count; i++) {
                    if (identities[i].IsTimingOut) return true;
                }
            }
            return false;
        }

        private bool IsTimedOut() {
            if (identities != null) {
                return identities.Any(i => i.IsTimedOut);
            }
            return false;
        }

        private void SetNotifyIcon(string iconPrefix) {
            if (iconPrefix != "") CurrentIcon = iconPrefix;
            string icon = "pack://application:,,/Assets/Images/ziti-" + CurrentIcon;
            if (state.UpdateAvailable) {
                icon += "-update";
            } else {
                if (IsTimedOut()) {
                    icon += "-mfa";
                } else {
                    if (IsTimingOut()) {
                        icon += "-timer";
                    }
                }
            }
            icon += ".ico";
            var iconUri = new Uri(icon);
            Stream iconStream = Application.GetResourceStream(iconUri).Stream;
            notifyIcon.Icon = new Icon(iconStream);

            Application.Current.MainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }

        private void LoadIdentities(Boolean repaint) {
            this.Dispatcher.Invoke(() => {
                for (int i = 0; i < IdList.Children.Count; i++) {
                    IdentityItem item = (IdentityItem)IdList.Children[i];
                    item.StopTimers();
                }
                IdList.Children.Clear();
                IdList.Height = 0;
                var desktopWorkingArea = SystemParameters.WorkArea;
                if (_maxHeight > (desktopWorkingArea.Height - 10)) {
                    _maxHeight = desktopWorkingArea.Height - 10;
                }
                if (_maxHeight < 100) {
                    _maxHeight = 100;
                }
                IdList.MaxHeight = _maxHeight - 520;
                ZitiIdentity[] ids = identities.OrderBy(i => (i.Name != null) ? i.Name.ToLower() : i.Name).ToArray();
                MainMenu.SetupIdList(ids);
                if (ids.Length > 0 && serviceClient.Connected) {
                    double height = defaultHeight + (ids.Length * 60);
                    if (height > _maxHeight) {
                        height = _maxHeight;
                    }
                    this.Height = height;
                    IdentityMenu.SetHeight(this.Height - 160);
                    MainMenu.IdentitiesButton.Visibility = Visibility.Visible;
                    foreach (var id in ids) {
                        IdentityItem idItem = new IdentityItem();
                        idItem.ShowError = ShowError;


                        idItem.ToggleStatus.IsEnabled = id.IsEnabled;
                        if (id.IsEnabled) idItem.ToggleStatus.Content = "ENABLED";
                        else idItem.ToggleStatus.Content = "DISABLED";

                        idItem.AuthenticateTOTP += IdItem_Authenticate;
                        idItem.OnStatusChanged += Id_OnStatusChanged;
                        idItem.Identity = id;
                        idItem.IdentityChanged += IdItem_IdentityChanged;
                        idItem.BlurbEvent += IdItem_BlurbEvent;
                        if (repaint) {
                            idItem.RefreshUI();
                        }
                        idItem.CompleteExternalAuth += CompleteExternalAuthEvent;

                        IdList.Children.Add(idItem);

                        if (IdentityMenu.Visibility == Visibility.Visible) {
                            if (id.Identifier == IdentityMenu.Identity.Identifier) IdentityMenu.Identity = id;
                        }
                    }
                    DoubleAnimation animation = new DoubleAnimation((double)(ids.Length * 64), TimeSpan.FromSeconds(.2));
                    IdList.BeginAnimation(FrameworkElement.HeightProperty, animation);
                    IdListScroller.Visibility = Visibility.Visible;
                } else {
                    this.Height = defaultHeight;
                    MainMenu.IdentitiesButton.Visibility = Visibility.Collapsed;
                    IdListScroller.Visibility = Visibility.Collapsed;

                }
                AddIdButton.Visibility = Visibility.Visible;
                AddIdAreaButton.Visibility = Visibility.Visible;

                Placement();
                SetNotifyIcon("");
            });
        }

        private async void IdItem_BlurbEvent(ZitiIdentity identity) {
            if(identity.AuthInProgress) {
                await ShowBlurbAsync("Authentication in progress", "Please check your browser");
            }
        }

        private void IdItem_IdentityChanged(ZitiIdentity identity) {
            for (int i = 0; i < identities.Count; i++) {
                if (identities[i].Identifier == identity.Identifier) {
                    identities[i] = identity;
                    break;
                }
            }
            SetNotifyIcon("");
        }

        private void IdItem_Authenticate(ZitiIdentity identity) {
            ShowAuthenticate(identity);
        }

        private void Id_OnStatusChanged(bool attached) {
            for (int i = 0; i < IdList.Children.Count; i++) {
                IdentityItem item = IdList.Children[i] as IdentityItem;
                if (item.ToggleSwitch.Enabled) break;
            }
        }

        private void TunnelConnected(bool isConnected) {
            this.Dispatcher.Invoke(() => {
                if (isConnected) {
                    ConnectButton.Visibility = Visibility.Collapsed;
                    DisconnectButton.Visibility = Visibility.Visible;
                    MainMenu.Connected();
                    HideLoad();
                    SetNotifyIcon("green");
                    props.Connected();
                } else {
                    ConnectButton.Visibility = Visibility.Visible;
                    DisconnectButton.Visibility = Visibility.Collapsed;
                    IdentityMenu.Visibility = Visibility.Collapsed;
                    MainMenu.Visibility = Visibility.Collapsed;
                    HideBlurb();
                    MainMenu.Disconnected();
                    DownloadSpeed.Content = "0.0";
                    UploadSpeed.Content = "0.0";
                    props.Disconnected();
                }
            });
        }

        private void SetLocation() {
            var desktopWorkingArea = SystemParameters.WorkArea;

            var renderedHeight = MainView.ActualHeight; // > defaultHeight ? MainView.ActualHeight : defaultHeight;
            IdentityMenu.MainHeight = renderedHeight;
            
            double defaultMiddle = 195;
            if (this.ActualWidth > 0) {
                defaultMiddle = this.ActualWidth / 2 - Arrow.ActualWidth / 2;
            }

            Rectangle trayRectangle = WinAPI.GetTrayRectangle();
            if (trayRectangle.Top < 20) {
                this.Position = "Top";
                this.Top = desktopWorkingArea.Top + _top;
                this.Left = desktopWorkingArea.Right - this.Width - _right;
                Arrow.SetValue(Canvas.TopProperty, (double)0);
                Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
                MainMenu.Arrow.SetValue(Canvas.TopProperty, (double)0);
                MainMenu.Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
                IdentityMenu.Arrow.SetValue(Canvas.TopProperty, (double)0);
                IdentityMenu.Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
            } else if (trayRectangle.Left < 20) {
                this.Position = "Left";
                this.Left = _left;
                this.Top = desktopWorkingArea.Bottom - this.ActualHeight - 75;
                Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                Arrow.SetValue(Canvas.LeftProperty, (double)0);
                MainMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                MainMenu.Arrow.SetValue(Canvas.LeftProperty, (double)0);
                IdentityMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                IdentityMenu.Arrow.SetValue(Canvas.LeftProperty, (double)0);
            } else if (desktopWorkingArea.Right == (double)trayRectangle.Left) {
                this.Position = "Right";
                this.Left = desktopWorkingArea.Right - this.Width - 20;
                this.Top = desktopWorkingArea.Bottom - renderedHeight - 75;
                Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                Arrow.SetValue(Canvas.LeftProperty, this.Width - 30);
                MainMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                MainMenu.Arrow.SetValue(Canvas.LeftProperty, this.Width - 30);
                IdentityMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 200);
                IdentityMenu.Arrow.SetValue(Canvas.LeftProperty, this.Width - 30);
            } else {
                this.Position = "Bottom";
                this.Left = desktopWorkingArea.Right - this.Width - 75;
                this.Top = desktopWorkingArea.Bottom - renderedHeight;
                Arrow.SetValue(Canvas.TopProperty, renderedHeight - 35);
                Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
                MainMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 35);
                MainMenu.Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
                IdentityMenu.Arrow.SetValue(Canvas.TopProperty, renderedHeight - 35);
                IdentityMenu.Arrow.SetValue(Canvas.LeftProperty, defaultMiddle);
            }
        }
        public void Placement() {
            if (_isAttached) {
                Arrow.Visibility = Visibility.Visible;
                IdentityMenu.Arrow.Visibility = Visibility.Visible;
                SetLocation();
            } else {
                IdentityMenu.Arrow.Visibility = Visibility.Visible;
                Arrow.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenIdentity(ZitiIdentity identity) {
            IdentityMenu.Identity = identity;
        }

        private void ShowMenu(object sender, MouseButtonEventArgs e) {
            MainMenu.Visibility = Visibility.Visible;
        }

        async private void AddId(EnrollIdentifierPayload payload) {
            try {
#if DEBUG
                Console.WriteLine("AddId.JwtContent\t: " + payload.JwtContent);
                Console.WriteLine("AddId.IdentityFilename\t: " + payload.IdentityFilename);
                Console.WriteLine("AddId.ControllerURL\t: " + payload.ControllerURL);
                Console.WriteLine("AddId.Certificate\t: " + payload.Certificate);
                Console.WriteLine("AddId.Key\t\t: " + payload.Key);
                Console.WriteLine("AddId.UseKeychain\t: " + payload.UseKeychain);
#endif
                Identity createdId = await serviceClient.AddIdentityAsync(payload);

                if (createdId != null) {
                    var zid = ZitiIdentity.FromClient(createdId);
                    AddIdentity(zid);
                    LoadIdentities(true);
                    await serviceClient.IdentityOnOffAsync(createdId.Identifier, true);
                } else {
                    // this never returns a value...
                }
            } catch (ServiceException se) {
                ShowError(se.Message, se.AdditionalInfo);
            } catch (Exception ex) {
                ShowError("Unexpected Error", "Code 2:" + ex.Message);
            }
            HideLoad();
        }
        private static string PadBase64(string base64) {
            int padding = 4 - (base64.Length % 4);
            if (padding < 4) {
                base64 += new string('=', padding);
            }
            return base64;
        }

        private void AddIdentity_Click(object sender, RoutedEventArgs e) {
            UIModel.HideOnLostFocus = false;
            OpenFileDialog jwtDialog = new OpenFileDialog();
            UIModel.HideOnLostFocus = true;
            jwtDialog.DefaultExt = ".jwt";
            jwtDialog.Filter = "Ziti Identities (*.jwt)|*.jwt";

            if (jwtDialog.ShowDialog() == true) {
                ShowLoad("Adding Identity", "Please wait while the identity is added");
                string fileContent = File.ReadAllText(jwtDialog.FileName);
                EnrollIdentifierPayload payload = new EnrollIdentifierPayload();
                payload.UseKeychain = Properties.Settings.Default.UseKeychain;
                string jwtFile = Path.GetFileName(jwtDialog.FileName);
                payload.IdentityFilename = Path.GetFileNameWithoutExtension(jwtFile);
                payload.JwtContent = fileContent.Trim();
                string[] jwtParts = fileContent?.Split('.');


                if (jwtParts != null && jwtParts.Length > 1) {
                    string jsonString = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(jwtParts[1])));

                    // Deserialize JSON into a dynamic object
                    dynamic jsonObj = JsonConvert.DeserializeObject(jsonString);
#if DEBUG
                    Console.WriteLine(jsonString);
                    // Access properties dynamically
                    Console.WriteLine($"ISS: {jsonObj.iss}");
                    Console.WriteLine($"SUB: {jsonObj.sub}");
                    Console.WriteLine($"JTI: {jsonObj.jti}");
                    Console.WriteLine($"AUD: {string.Join(", ", jsonObj.aud)}");
                    Console.WriteLine($"EM: {jsonObj.em}");
#endif
                    switch ($"{jsonObj.em}") {
                        case "ottca":
                            With3rdPartyCA_Click(sender, e);
                            break;
                        case "network":
                            AddId(payload);
                            break;
                        case "ott":
                            AddId(payload);
                            break;
                        case "ca":
                            HideLoad();
                            AddIdentityBy3rdPartyCA.Payload = payload;
                            ShowJoinWith3rdPartyCA();
                            break;
                    }
                } else {
                    // invalid jwt
                    logger.Error("JWT is invalid? {}", fileContent);
                }
                HideLoad();
            }
        }

        private void OnTimedEvent(object sender, EventArgs e) {
            TimeSpan span = (DateTime.Now - _startDate);
            int hours = span.Hours;
            int minutes = span.Minutes;
            int seconds = span.Seconds;
            var hoursString = (hours > 9) ? hours.ToString() : "0" + hours;
            var minutesString = (minutes > 9) ? minutes.ToString() : "0" + minutes;
            var secondsString = (seconds > 9) ? seconds.ToString() : "0" + seconds;
            ConnectedTime.Content = hoursString + ":" + minutesString + ":" + secondsString;
        }

        private void InitializeTimer(int millisAgoStarted) {
            _startDate = DateTime.Now.Subtract(new TimeSpan(0, 0, 0, 0, millisAgoStarted));
            _tunnelUptimeTimer = new System.Windows.Forms.Timer();
            _tunnelUptimeTimer.Interval = 100;
            _tunnelUptimeTimer.Tick += OnTimedEvent;
            _tunnelUptimeTimer.Enabled = true;
            _tunnelUptimeTimer.Start();
        }

        async private void Disconnect(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            try {
                ShowLoad("Disabling Service", "Please wait for the service to stop.");
                var r = await monitorClient.StopServiceAsync();
                if (r.Code != 0) {
                    logger.Warn("ERROR: Error:{0}, Message:{1}", r.Error, r.Message);
                } else {
                    logger.Info("Service stopped!");
                    SetNotifyIcon("white");
                }
            } catch (MonitorServiceException me) {
                logger.Warn("the monitor service appears offline. {0}", me);
                ShowError("Error Disabling Service", "The monitor service is offline");
            } catch (Exception ex) {
                logger.Error(ex, "unexpected error: {0}", ex.Message);
                ShowError("Error Disabling Service", "An error occurred while trying to disable the data service. Is the monitor service running?");
            }
            HideLoad();
        }

        internal void ShowLoad(string title, string msg) {
            this.Dispatcher.Invoke(() => {
                LoadingDetails.Text = msg;
                LoadingTitle.Content = title;
                LoadProgress.IsIndeterminate = true;
                LoadingScreen.Visibility = Visibility.Visible;
                UpdateLayout();
            });
        }

        internal void HideLoad() {
            this.Dispatcher.Invoke(() => {
                LoadingScreen.Visibility = Visibility.Collapsed;
                LoadProgress.IsIndeterminate = false;
            });
        }

        private void FormFadeOut_Completed(object sender, EventArgs e) {
            closeCompleted = true;
        }
        private bool closeCompleted = false;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            if (!closeCompleted) {
                FormFadeOut.Begin();
                e.Cancel = true;
            }
        }

        public void ShowError(string title, string message) {
            this.Dispatcher.Invoke(() => {
                ErrorTitle.Text = title;
                ErrorDetails.Text = message;
                ErrorView.Visibility = Visibility.Visible;
            });
        }

        private void CloseError(object sender, RoutedEventArgs e) {
            this.Dispatcher.Invoke(() => {
                ErrorView.Visibility = Visibility.Collapsed;
                NoServiceView.Visibility = Visibility.Collapsed;
                CloseErrorButton.IsEnabled = true;
            });
        }

        private void CloseApp(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            Application.Current.Shutdown();
        }

        private void MainUI_Deactivated(object sender, EventArgs e) {
            if (this._isAttached) {
#if DEBUG
                logger.Debug("debug is enabled - windows pinned");
#else
				this.Visibility = Visibility.Collapsed;
#endif
            }
        }

        int cur = 0;
        LogLevelEnum[] levels = new LogLevelEnum[] { LogLevelEnum.FATAL, LogLevelEnum.ERROR, LogLevelEnum.WARN, LogLevelEnum.INFO, LogLevelEnum.DEBUG, LogLevelEnum.TRACE, LogLevelEnum.VERBOSE };
        public LogLevelEnum NextLevel() {
            cur++;
            if (cur > 6) {
                cur = 0;
            }
            return levels[cur];
        }

        private void IdList_LayoutUpdated(object sender, EventArgs e) {
            Placement();
        }

        async private void CollectLogFileClick(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            await CollectLogFiles();
        }
        async private Task CollectLogFiles() {
            MonitorServiceStatusEvent resp = await monitorClient.CaptureLogsAsync();
            if (resp != null) {
                logger.Info("response: {0}", resp.Message);
            } else {
                ShowError("Error Collecting Feedback", "An error occurred while trying to gather feedback. Is the monitor service running?");
            }
        }

        /// <summary>
        /// Show the blurb as a growler notification
        /// </summary>
        /// <param name="message">The message to show</param>
        /// <param name="url">The url or action name to execute</param>
        public async Task ShowBlurbAsync(string message, string url, string level = "error") {
            try {
                RedBlurb.Visibility = Visibility.Collapsed;
                InfoBlurb.Visibility = Visibility.Collapsed;
                if (level == "error") {
                    RedBlurb.Visibility = Visibility.Visible;
                } else {
                    InfoBlurb.Visibility = Visibility.Visible;
                }
                Blurb.Content = message;
                _blurbUrl = url;
                BlurbArea.Visibility = Visibility.Visible;
                BlurbArea.Opacity = 0;
                BlurbArea.Margin = new Thickness(0, 0, 0, 0);
                DoubleAnimation animation = new DoubleAnimation(1, TimeSpan.FromSeconds(.3));
                ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(15, 0, 15, 15), TimeSpan.FromSeconds(.3));
                BlurbArea.BeginAnimation(Grid.OpacityProperty, animation);
                BlurbArea.BeginAnimation(Grid.MarginProperty, animateThick);
                await Task.Delay(2500);
                HideBlurb();
            } catch(Exception e) {
                logger.Error(e);
            }
        }

        /// <summary>
        /// Execute the hide operation wihout an action from the growler
        /// </summary>
        /// <param name="sender">The object that was clicked</param>
        /// <param name="e">The click event</param>
        private void DoHideBlurb(object sender, MouseButtonEventArgs e) {
            HideBlurb();
        }

        /// <summary>
        /// Hide the blurb area
        /// </summary>
        private void HideBlurb() {
            DoubleAnimation animation = new DoubleAnimation(0, TimeSpan.FromSeconds(.3));
            ThicknessAnimation animateThick = new ThicknessAnimation(new Thickness(0, 0, 0, 0), TimeSpan.FromSeconds(.3));
            animation.Completed += HideComplete;
            BlurbArea.BeginAnimation(Grid.OpacityProperty, animation);
            BlurbArea.BeginAnimation(Grid.MarginProperty, animateThick);
        }

        /// <summary>
        /// Hide the blurb area after the animation fades out
        /// </summary>
        /// <param name="sender">The animation object</param>
        /// <param name="e">The completion event</param>
        private void HideComplete(object sender, EventArgs e) {
            BlurbArea.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Execute a predefined action or url when the pop up is clicked
        /// </summary>
        /// <param name="sender">The object that was clicked</param>
        /// <param name="e">The click event</param>
        private void BlurbAction(object sender, MouseButtonEventArgs e) {
            if (_blurbUrl.Length > 0) {
                // So this simply execute a url but you could do like if (_blurbUrl=="DoSomethingNifty") CallNifyFunction();
                if (_blurbUrl == this.RECOVER) {
                    this.ShowMFA(IdentityMenu.Identity, 4);
                } else {
                    Process.Start(new ProcessStartInfo(_blurbUrl) { UseShellExecute = true });
                }
                HideBlurb();
            } else {
                HideBlurb();
            }
        }

        private void ShowAuthenticate(ZitiIdentity identity) {
            MFAAuthenticate(identity);
        }

        private void ShowRecovery(ZitiIdentity identity) {
            ShowMFARecoveryCodes(identity);
        }

        private void DoLoading(bool isComplete) {
            if (isComplete) HideLoad();
            else ShowLoad("Loading", "Please Wait.");
        }

        private void AddIdentityContextMenu(object sender, MouseButtonEventArgs e) {
            var stackPanel = sender as StackPanel;
            if (stackPanel?.ContextMenu != null) {
                stackPanel.ContextMenu.PlacementTarget = stackPanel;
                stackPanel.ContextMenu.IsOpen = true;
            }
        }

        private void WithJwt_Click(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            // Handle "With JWT"
            AddIdentity_Click(sender, e);
        }

        void WithUrl_Click(object sender, RoutedEventArgs e) {
            ShowJoinByUrl();
        }
        void With3rdPartyCA_Click(object sender, RoutedEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            ShowJoinWith3rdPartyCA();
        }

        void OnAddIdentityAction(EnrollIdentifierPayload payload, UserControl toClose) {
            CloseJoinByUrl(false, toClose);
            AddId(payload);
        }

        private async void CompleteExternalAuthEvent(ZitiIdentity identity, string provider) {
            try {
                DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
                if (identity?.ExtAuthProviders?.Count > 0) {
                    ExternalAuthLoginResponse resp = await serviceClient.ExternalAuthLogin(identity.Identifier, provider);
                    if (resp?.Error == null) {
                        if (resp?.Data?.url != null) {
                            Console.WriteLine(resp.Data?.url);
                            Process.Start(resp.Data.url);
                        } else {
                            Console.WriteLine("The response contained no url???");
                        }
                    } else {
                        ShowError("Failed to Authenticate", resp.Error);
                    }
                } else {
                    ShowError("Failed to Authenticate", "No external providers found! This is a configuration error. Inform your network administrator.");
                }
            } catch (Exception ex) {
                logger.Error("unexpected error!", ex);
            }
        }

        private void MainUI_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            UIUtils.ClickedControl = e.Source as UIElement;
        }
    }

    public class ActionCommand : ICommand {
        private readonly Action _action;

        public ActionCommand(Action action) {
            _action = action;
        }

        public void Execute(object parameter) {
            _action();
        }

        public bool CanExecute(object parameter) {
            return true;
        }
#pragma warning disable CS0067 //The event 'ActionCommand.CanExecuteChanged' is never used
        public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067 //The event 'ActionCommand.CanExecuteChanged' is never used
    }

    public class MainViewModel : INotifyPropertyChanged {
        private string _connectLabelContent = "Tap to Connect";

        public string ConnectLabelContent {
            get { return _connectLabelContent; }
            set {
                _connectLabelContent = value;
                OnPropertyChanged(nameof(ConnectLabelContent));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Disconnected() {
            ConnectLabelContent = "Tap to Connect";
        }

        public void Connected() {
            ConnectLabelContent = "Tap to Disconnect";
        }
    }
}
