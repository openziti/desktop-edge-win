using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.IO;
using ZitiDesktopEdge.Models;
using System.IO.Compression;

using ZitiDesktopEdge.ServiceClient;
using System.ServiceProcess;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics;
using System.Security.Principal;
using System.Net;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ZitiDesktopEdge {

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow:Window {

		public System.Windows.Forms.NotifyIcon notifyIcon;
		private DateTime _startDate;
		private System.Windows.Forms.Timer _timer;
		private Client serviceClient = null;
		private bool _isAttached = true;
		private int _right = 75;
		private int _bottom = 0;
		private double _maxHeight = 800d;
		private string[] suffixes = { "bps", "kbps", "mbps", "gbps", "tbps", "pbps" };

		private List<ZitiIdentity> identities {
			get {
				return (List<ZitiIdentity>)Application.Current.Properties["Identities"];
			}
		}

		private void LaunchOrInstall() {
			ServiceController ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName=="ziti");
			if (ctl==null) {
				SetCantDisplay();
			} else {
				if (ctl.Status!=ServiceControllerStatus.Running) {
					try {
						ctl.Start();
					} catch (Exception e) {
						UILog.Log(e.Message);
						SetCantDisplay();
					}
				}
			}
		}

		private List<ZitiService> services = new List<ZitiService>();
		public MainWindow() {
			InitializeComponent();

			App.Current.MainWindow.WindowState = WindowState.Normal;
			App.Current.MainWindow.Closing += MainWindow_Closing;
			App.Current.MainWindow.Deactivated += MainWindow_Deactivated;
			App.Current.MainWindow.Activated += MainWindow_Activated;
			notifyIcon = new System.Windows.Forms.NotifyIcon();
			notifyIcon.Visible = true;
			notifyIcon.Click += TargetNotifyIcon_Click;
			notifyIcon.Visible = true;

			LaunchOrInstall();

			SetNotifyIcon("white");
			InitializeComponent();
		}

		private void MainWindow_Activated(object sender, EventArgs e) {
			this.Visibility = Visibility.Visible;
			Debug.WriteLine("Activation");
			Placement();
		}

		private void MainWindow_Deactivated(object sender, EventArgs e) {
#if DEBUG
				Debug.WriteLine("debug is enabled - windows pinned");
#else
				this.Visibility = Visibility.Collapsed;
#endif
		}

		private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
			notifyIcon.Visible = false;
			notifyIcon.Icon.Dispose();
			notifyIcon.Dispose();
		}
		
		private void SetCantDisplay(string msg, string detailMessage) {
			NoServiceView.Visibility = Visibility.Visible;
			ErrorMsg.Content = msg;
			ErrorMsgDetail.Content = detailMessage;
			SetNotifyIcon("red");
		}
		private void SetCantDisplay() {
			SetCantDisplay("Service Not Started", "Start the Ziti Tunnel Service to get started");
		}

		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (!_isAttached&&e.ChangedButton == MouseButton.Left) this.DragMove();
		}

		private void TargetNotifyIcon_Click(object sender, EventArgs e) {
			this.Show();
			this.Activate();
		}

		private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
			Placement();
			// add a new service client
			serviceClient = new Client();
			serviceClient.OnClientConnected += ServiceClient_OnClientConnected;
			serviceClient.OnClientDisconnected += ServiceClient_OnClientDisconnected;
			serviceClient.OnIdentityEvent += ServiceClient_OnIdentityEvent;
			serviceClient.OnMetricsEvent += ServiceClient_OnMetricsEvent;
			serviceClient.OnServiceEvent += ServiceClient_OnServiceEvent;
			serviceClient.OnTunnelStatusEvent += ServiceClient_OnTunnelStatusEvent;

			Application.Current.Properties.Add("ServiceClient", serviceClient);
			Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
			MainMenu.OnAttachmentChange += AttachmentChanged;
			IdentityMenu.OnError += IdentityMenu_OnError;

			try {
				serviceClient.Connect();
				//var s = serviceClient.GetStatus();
				//LoadStatusFromService(s.Status);
			} catch /*ignored for now (Exception ex) */{
				SetCantDisplay();
				serviceClient.Reconnect();
			}
			Debug.WriteLine("App Loaded");
			IdentityMenu.OnForgot += IdentityForgotten;
		}

		private void IdentityMenu_OnError(string message) {
			ShowError("Identity Error", message);
		}

		private void ServiceClient_OnClientConnected(object sender, object e) {
			this.Dispatcher.Invoke(() => {
				//e is _ALWAYS_ null at this time use this to display something if you want
				NoServiceView.Visibility = Visibility.Collapsed;
				SetNotifyIcon("white");
				LoadIdentities(true);
			});
		}

		private void ServiceClient_OnClientDisconnected(object sender, object e) {
			this.Dispatcher.Invoke(() => {
				SetCantDisplay();
			});
		}

		private void ServiceClient_OnIdentityEvent(object sender, IdentityEvent e) {
			if (e == null) return;

			ZitiIdentity zid = ZitiIdentity.FromClient(e.Id);
			Debug.WriteLine($"==== IdentityEvent    : action:{e.Action} fingerprint:{e.Id.FingerPrint} name:{e.Id.Name} ");

			this.Dispatcher.Invoke(() => {
				if (e.Action == "added") {
					var found = identities.Find(i => i.Fingerprint == e.Id.FingerPrint);
					if (found == null) {
						identities.Add(zid);
						LoadIdentities(true);
					} else {
						//if we get here exit out so that LoadIdentities() doesn't get called
						found.IsEnabled = true;
						return;
					}
				} else {
					IdentityForgotten(ZitiIdentity.FromClient(e.Id));
				}
			});
			Debug.WriteLine($"IDENTITY EVENT. Action: {e.Action} fingerprint: {zid.Fingerprint}");
		}

		private void ServiceClient_OnMetricsEvent(object sender, List<Identity> ids) {
			if (ids != null) {
				long totalUp = 0;
				long totalDown = 0;
				foreach (var id in ids) {
					//Debug.WriteLine($"==== MetricsEvent     : id {id.Name} down: {id.Metrics.Down} up:{id.Metrics.Up}");
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
			
			Debug.WriteLine($"==== ServiceEvent     : action:{e.Action} fingerprint:{e.Fingerprint} name:{e.Service.Name} ");
			this.Dispatcher.Invoke(() => {
				var found = identities.Find(id => id.Fingerprint == e.Fingerprint);

				if (found == null) {
					Debug.WriteLine($"{e.Action} service event for {e.Service.Name} but the provided identity fingerprint {e.Fingerprint} is not found!");
					return;
				}

				if (e.Action == "added") {
					ZitiService zs = new ZitiService(e.Service);
					var svc = found.Services.Find(s => s.Name == zs.Name);
					if (svc == null) found.Services.Add(zs);
					else Debug.WriteLine("the service named " + zs.Name + " is already accounted for on this identity.");
				} else {
					Debug.WriteLine("removing the service named: " + e.Service.Name);
					found.Services.RemoveAll(s => s.Name == e.Service.Name);
				}
				LoadIdentities(false);
			});
		}

		private void ServiceClient_OnTunnelStatusEvent(object sender, TunnelStatusEvent e) {
			if (e == null) return; //just skip it for now...
			Debug.WriteLine($"==== TunnelStatusEvent: ");
			this.Dispatcher.Invoke(() => {
				if(e.ApiVersion != Client.EXPECTED_API_VERSION) {
					SetCantDisplay("Version mismatch!", "The version of the Service is not compatible");
					return;
                }
				
				InitializeTimer((int)e.Status.Duration);
				LoadStatusFromService(e.Status);
				LoadIdentities(false);
			});
		}

		private void IdentityForgotten(ZitiIdentity forgotten) {
			ZitiIdentity idToRemove = null;
			foreach (var id in identities) {
				if (id.Fingerprint == forgotten.Fingerprint) {
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
				var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
				this.Left = desktopWorkingArea.Right - this.Width - 75;
				this.Top = desktopWorkingArea.Bottom - this.Height - 75;
			}
			Placement();
			MainMenu.Visibility = Visibility.Collapsed;
		}

		private void LoadStatusFromService(TunnelStatus status) {
			//clear any identities
			this.identities.Clear();

			if (status != null) {
				NoServiceView.Visibility = Visibility.Collapsed;
				SetNotifyIcon("white");
				if (status.Active) {
					InitializeTimer((int)status.Duration);
					ConnectButton.Visibility = Visibility.Collapsed;
					DisconnectButton.Visibility = Visibility.Visible;
					SetNotifyIcon("green");
				} else {
					ConnectButton.Visibility = Visibility.Visible;
					DisconnectButton.Visibility = Visibility.Collapsed;
				}
				if (!Application.Current.Properties.Contains("ip")) {
					Application.Current.Properties.Add("ip", status?.IpInfo?.Ip);
				}
				if (!Application.Current.Properties.Contains("subnet")) {
					Application.Current.Properties.Add("subnet", status?.IpInfo?.Subnet);
				}
				if (!Application.Current.Properties.Contains("mtu")) {
					Application.Current.Properties.Add("mtu", status?.IpInfo?.MTU);
				}
				if (!Application.Current.Properties.Contains("dns")) {
					Application.Current.Properties.Add("dns", status?.IpInfo?.DNS);
				}

				foreach (var id in status.Identities) {
					updateViewWithIdentity(id);
				}
				Debug.WriteLine("Load From Service");
				LoadIdentities(true);
			} else {
				SetCantDisplay();
			}
		}

		private void updateViewWithIdentity(Identity id) {
			var zid = ZitiIdentity.FromClient(id);
			foreach (var i in identities) {
				if (i.Fingerprint == zid.Fingerprint) {
					identities.Remove(i);
					break;
				}
			}
			identities.Add(zid);
		}
		private void SetNotifyIcon(string iconPrefix) {
			var iconUri = new Uri("pack://application:,,/Assets/Images/ziti-" + iconPrefix + ".ico");
			System.IO.Stream iconStream = System.Windows.Application.GetResourceStream(iconUri).Stream;
			notifyIcon.Icon = new System.Drawing.Icon(iconStream);

			Application.Current.MainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
		}

		private void LoadIdentities(Boolean repaint) {
			IdList.Children.Clear();
			IdList.Height = 0;
			IdList.MaxHeight = _maxHeight-520;
			ZitiIdentity[] ids = identities.ToArray();
			double height = 460 + (ids.Length * 60);
			if (height > _maxHeight) height = _maxHeight;
			this.Height = height;
			IdentityMenu.SetHeight(this.Height-160);
			for (int i=0; i<ids.Length; i++) {
				IdentityItem id = new IdentityItem();
				if (ids[i].IsEnabled) {
					SetNotifyIcon("green");
					ConnectButton.Visibility = Visibility.Collapsed;
					DisconnectButton.Visibility = Visibility.Visible;
				}
				id.Identity = ids[i];
				//id.OnClick += OpenIdentity;
				IdList.Children.Add(id);
				IdList.Height += 60;
			}
			if (this._isAttached&&repaint) Placement();
		}

		public void Placement() {
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			if (_isAttached) {
				Arrow.Visibility = Visibility.Visible;
				this.Left = desktopWorkingArea.Right - this.Width - _right;
				this.Top = desktopWorkingArea.Bottom - this.Height - _bottom;
			} else {
				Arrow.Visibility = Visibility.Collapsed;
			}
			Debug.WriteLine("Placement: " + this.Left + " " + desktopWorkingArea.Right + " " + this.Width + " " + _right);
			Debug.WriteLine("Place: " + this.Top + " " + desktopWorkingArea.Bottom + " " + this.Height + " " + _bottom);
		}

		private void OpenIdentity(ZitiIdentity identity) {
			IdentityMenu.Identity = identity;
			
		}

		private void ShowMenu(object sender, MouseButtonEventArgs e) {
			MainMenu.Visibility = Visibility.Visible;
		}

		private void AddIdentity(object sender, MouseButtonEventArgs e) {
			UIModel.HideOnLostFocus = false;
			Microsoft.Win32.OpenFileDialog jwtDialog = new Microsoft.Win32.OpenFileDialog();
			UIModel.HideOnLostFocus = true;
			jwtDialog.DefaultExt = ".jwt";
			jwtDialog.Filter = "Ziti Identities (*.jwt)|*.jwt";
			if (jwtDialog.ShowDialog() == true) {
				ShowLoad();
				string fileContent = File.ReadAllText(jwtDialog.FileName);
				
				try {
					Identity createdId = serviceClient.AddIdentity(System.IO.Path.GetFileName(jwtDialog.FileName), false, fileContent);
					ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
					client.IdentityOnOff(createdId.FingerPrint, true);
					if (createdId != null) {
						identities.Add(ZitiIdentity.FromClient(createdId));
						LoadIdentities(true);
					} else {
						ShowError("Identity Error", "Identity Id was null, please try again");
					}
				} catch (ServiceException se) {
					ShowError(se.AdditionalInfo, se.Message);
				} catch (Exception ex) {
					ShowError("Unexpected Error", "Code 2:" + ex.Message);
				}
				HideLoad();
			}
		}

		private void OnTimedEvent(object sender, EventArgs e) {
			TimeSpan span = (DateTime.Now - _startDate);
			int hours = span.Hours;
			int minutes = span.Minutes;
			int seconds = span.Seconds;
			var hoursString = (hours>9)?hours.ToString():"0"+hours;
			var minutesString = (minutes>9)? minutes.ToString():"0"+minutes;
			var secondsString = (seconds>9) ? seconds.ToString() : "0"+seconds;
			ConnectedTime.Content = hoursString+":"+minutesString+":"+secondsString;
		}

		private void InitializeTimer(int millisAgoStarted) {
			_startDate = DateTime.Now.Subtract(new TimeSpan(0,0,0,0, millisAgoStarted));
			_timer = new System.Windows.Forms.Timer();
			_timer.Interval = 100;
			_timer.Tick += OnTimedEvent;
			_timer.Enabled = true;
			_timer.Start();
		}
		private void Connect(object sender, RoutedEventArgs e)
		{
			this.Dispatcher.Invoke(() =>
			{
				ShowLoad();
				//Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.ContextIdle);
				DoConnect();
				HideLoad();
			});
		}

		private void DoConnect() {
			try {
				serviceClient.SetTunnelState(true);
				SetNotifyIcon("green");
				ConnectButton.Visibility = Visibility.Collapsed;
				DisconnectButton.Visibility = Visibility.Visible;

				for (int i = 0; i < identities.Count; i++) {
					serviceClient.IdentityOnOff(identities[i].Fingerprint, true);
				}
				for (int i = 0; i < IdList.Children.Count; i++) {
					IdentityItem item = IdList.Children[i] as IdentityItem;
					item._identity.IsEnabled = true;
					item.RefreshUI();
				}
			} catch (ServiceException se) {
				ShowError(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				ShowError("Unexpected Error", "Code 3:" + ex.Message);
			}
		}
		private void Disconnect(object sender, RoutedEventArgs e) {
			ShowLoad();
			try {
				ConnectedTime.Content =  "00:00:00";
				_timer.Stop();
				serviceClient.SetTunnelState(false);
				SetNotifyIcon("white");
				ConnectButton.Visibility = Visibility.Visible;
				DisconnectButton.Visibility = Visibility.Collapsed;
				for (int i = 0; i < identities.Count; i++) {
					serviceClient.IdentityOnOff(identities[i].Fingerprint, false);
				}
				for (int i = 0; i < IdList.Children.Count; i++) {
					IdentityItem item = IdList.Children[i] as IdentityItem;
					item._identity.IsEnabled = false;
					item.RefreshUI();
				}
			} catch (ServiceException se) {
				ShowError(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				ShowError("Unexpected Error", "Code 4:"+ex.Message);
			}
			HideLoad();
		}

		private void ShowLoad() {
			LoadProgress.IsIndeterminate = true;
			LoadingScreen.Visibility = Visibility.Visible;
		}

		private void HideLoad() {
			LoadingScreen.Visibility = Visibility.Collapsed;
			LoadProgress.IsIndeterminate = false;
		}

		private void FormFadeOut_Completed(object sender, EventArgs e) {
			closeCompleted = true;
			//this.Close();
		}
		private bool closeCompleted = false;
		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
			if (!closeCompleted) {
				FormFadeOut.Begin();
				e.Cancel = true;
			}
		}

		private void ShowError(String title, String message) {
			ErrorTitle.Content = title;
			ErrorDetails.Text = message;
			ErrorView.Visibility = Visibility.Visible;
		}

		private void CloseError(object sender, MouseButtonEventArgs e) {
			ErrorView.Visibility = Visibility.Collapsed;
		}

		private void CloseApp(object sender, MouseButtonEventArgs e) {
			Application.Current.Shutdown();
		}

		private void MainUI_Deactivated(object sender, EventArgs e) {
			if (UIModel.HideOnLostFocus) {
#if DEBUG
				Debug.WriteLine("debug is enabled - windows pinned");
#else
				this.Visibility = Visibility.Collapsed;
#endif
			}
		}

		private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
			Placement();
		}
	}
}
