using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.IO;
using ZitiTunneler.Models;

using ZitiTunneler.ServiceClient;
using System.ServiceProcess;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Diagnostics;

namespace ZitiTunneler {

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

		private List<ZitiIdentity> identities {
			get {
				return (List<ZitiIdentity>)Application.Current.Properties["Identities"];
			}
		}

		private List<ZitiService> services = new List<ZitiService>();
		public MainWindow() {
			InitializeComponent();
			App.Current.MainWindow.WindowState = WindowState.Normal;

			notifyIcon = new System.Windows.Forms.NotifyIcon();
			notifyIcon.Visible = true;
			notifyIcon.Click += TargetNotifyIcon_Click;
			notifyIcon.Visible = true;
			notifyIcon.ShowBalloonTip(5000, "Test", "Testing", System.Windows.Forms.ToolTipIcon.Info);

			ServiceController ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName=="ziti");
			if (ctl==null) {
				ProcessStartInfo installService = new ProcessStartInfo();
				installService.CreateNoWindow = true;
				installService.UseShellExecute = false;
				installService.FileName = Path.Combine(Environment.CurrentDirectory, "Service")+@"\ziti-tunnel.exe";
				installService.WindowStyle = ProcessWindowStyle.Hidden;
				installService.Arguments =" install";

				try {
					using (Process exeProcess = Process.Start(installService)) {
						exeProcess.WaitForExit();
					}
					ctl = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName=="ziti");
					if (ctl.Status!=ServiceControllerStatus.Running) {
						try {
							ctl.Start();
						} catch (Exception e) {
							SetCantDisplay();
						}
					}
				} catch (Exception e) {
					MessageBox.Show(e.ToString());
				}
			} else {
				if (ctl.Status!=ServiceControllerStatus.Running) {
					try {
						ctl.Start();
					} catch (Exception e) {
						SetCantDisplay();
					}
				}
			}

			SetNotifyIcon("white");
			InitializeComponent();
		}

		private void SetCantDisplay() {
			NoServiceView.Visibility=Visibility.Visible;
			SetNotifyIcon("red");
		}

		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (!_isAttached&&e.ChangedButton == MouseButton.Left) this.DragMove();
		}

		private void Repaint() {
			LoadIdentities();
		}

		private void TargetNotifyIcon_Click(object sender, EventArgs e) {
			this.Show();
			this.Activate();
			/*
			if (App.Current.MainWindow.WindowState==WindowState.Minimized) {
				App.Current.MainWindow.WindowState = WindowState.Normal;
				App.Current.MainWindow.BringIntoView();
				//this.Opacity = 1;
				//this.Activate();
			} else {
				App.Current.MainWindow.WindowState = WindowState.Minimized;
				this.Close();
				//this.Opacity = 0;
			}
			*/
		}

		private void MainWindow1_Loaded(object sender, RoutedEventArgs e) {
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			this.Left = desktopWorkingArea.Right - this.Width - _right;
			this.Top = desktopWorkingArea.Bottom - this.Height - _bottom;
			// add a new service client
			serviceClient = new Client();
			serviceClient.OnTunnelStatusUpdate += ServiceClient_OnTunnelStatusUpdated;
			serviceClient.OnMetricsUpdate += ServiceClient_OnMetricsUpdate;
			Application.Current.Properties.Add("ServiceClient", serviceClient);
			Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
			MainMenu.OnAttachmentChange += AttachmentChanged;
			try {
				LoadStatusFromService();
			} catch (Exception ex) {
				SetCantDisplay();
			}
			LoadIdentities();
			IdentityMenu.OnForgot += IdentityForgotten;
		}

<<<<<<< HEAD
		private void ServiceClient_OnMetricsUpdate(object sender, Metrics e) {
			this.Dispatcher.Invoke(() => {
				DownloadSpeed.Content = (e.Down / 1000).ToString();
				UploadSpeed.Content = (e.Up / 1000).ToString();
			});
		}
		private void ServiceClient_OnTunnelStatusUpdated(object sender, TunnelStatus e) {
=======
		private void ServiceClient_OnMetricsUpdate(object sender, Metrics e)
		{
			if (e != null) {
				this.Dispatcher.Invoke(() =>
				{
					DownloadSpeed.Content = (e.Down / 1000).ToString();
					UploadSpeed.Content = (e.Up / 1000).ToString();
				});
			}
		}

		private void ServiceClient_OnTunnelStatusUpdated(object sender, TunnelStatus e)
		{
			if (e != null)
			{
				long totalUp = 0;
				long totalDown = 0;
				foreach (var id in e.Identities)
				{
					System.Diagnostics.Debug.WriteLine($"id {id.Name} down: {totalDown} up:{totalUp}");
					if (id?.Metrics != null)
					{
						totalDown += id.Metrics.Down;
						totalUp += id.Metrics.Up;
					}
				}
				this.Dispatcher.Invoke(() =>
				{
					System.Diagnostics.Debug.WriteLine($"Triggering update of total down: {totalDown} up:{totalUp}");
					DownloadSpeed.Content = (totalDown / 1000).ToString();
					UploadSpeed.Content = (totalUp / 1000).ToString();
				});
			}
>>>>>>> origin/bug-fixing
		}

		private void IdentityForgotten(ZitiIdentity forgotten) {
			TunnelStatus status = serviceClient.GetStatus().Status;
			identities.Clear();
			foreach (var id in status.Identities) {
				var zid = ZitiIdentity.FromClient(id);
				identities.Add(zid);
			}
			LoadIdentities();
		}

		private void AttachmentChanged(bool attached) {
			_isAttached = attached;
			if (_isAttached) {
				Arrow.Visibility = Visibility.Visible;
				var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
				this.Left = desktopWorkingArea.Right-this.Width-_right;
				this.Top = desktopWorkingArea.Bottom-this.Height-_bottom;
			} else {
				Arrow.Visibility = Visibility.Collapsed;
				var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
				this.Left = desktopWorkingArea.Right-this.Width-75;
				this.Top = desktopWorkingArea.Bottom-this.Height-75;
			}
			MainMenu.Visibility = Visibility.Collapsed;
		}

		private void LoadStatusFromService() {
			var s = serviceClient.GetStatus();
			TunnelStatus status = s.Status;
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
				Application.Current.Properties.Add("ip", status?.IpInfo?.Ip);
				Application.Current.Properties.Add("subnet", status?.IpInfo?.Subnet);
				Application.Current.Properties.Add("mtu", status?.IpInfo?.MTU);
				Application.Current.Properties.Add("dns", status?.IpInfo?.DNS);

				foreach (var id in status.Identities) {
					updateViewWithIdentity(id);
				}
				LoadIdentities();
			} else {
				SetCantDisplay();
			}
		}

		private void updateViewWithIdentity(Identity id) {
			var zid = ZitiIdentity.FromClient(id);
			identities.Add(zid);
		}
		private void SetNotifyIcon(string iconPrefix) {
			System.IO.Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,/Assets/Images/ziti-"+iconPrefix+".ico")).Stream;
			notifyIcon.Icon = new System.Drawing.Icon(iconStream);
		}

		private void LoadIdentities() {
			IdList.Children.Clear();
			IdList.Height = 0;
			IdList.MaxHeight = _maxHeight-520;
			ZitiIdentity[] ids = identities.ToArray();
			this.Height = 460+(ids.Length*60);
			if (this.Height>_maxHeight) this.Height = _maxHeight;
			IdentityMenu.SetHeight(this.Height-160);
			for (int i=0; i<ids.Length; i++) {
				IdentityItem id = new IdentityItem();
				id.Identity = ids[i];
				id.OnClick += OpenIdentity;
				IdList.Children.Add(id);
				IdList.Height += 60;
			}
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			this.Left = desktopWorkingArea.Right-this.Width-_right;
			this.Top = desktopWorkingArea.Bottom-this.Height-_bottom;
		}

		private void OpenIdentity(ZitiIdentity identity) {
			IdentityMenu.Identity = identity;
		}

		private void ShowMenu(object sender, MouseButtonEventArgs e) {
			MainMenu.Visibility = Visibility.Visible;
		}

		private void AddIdentity(object sender, MouseButtonEventArgs e) {
			Microsoft.Win32.OpenFileDialog jwtDialog = new Microsoft.Win32.OpenFileDialog();
			jwtDialog.DefaultExt = ".jwt";
			jwtDialog.Filter = "Ziti Identities (*.jwt)|*.jwt";
			if (jwtDialog.ShowDialog() == true) {
				string fileContent = File.ReadAllText(jwtDialog.FileName);
				
				try {
					Identity createdId = serviceClient.AddIdentity(System.IO.Path.GetFileName(jwtDialog.FileName), false, fileContent);
					ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
					client.IdentityOnOff(createdId.FingerPrint, true);
					if (createdId != null) {
						identities.Add(ZitiIdentity.FromClient(createdId));
						LoadIdentities();
						//MessageBox.Show("New identity added with fingerprint: " + createdId.FingerPrint);
						//updateViewWithIdentity(createdId);
					} else {
						// Jeremy buddy - error popup here
						MessageBox.Show("created id was null - wtf jeremy. your fault, um nope your fault clint, or probably Andrews");
					}
				} catch (ServiceException se) {
					MessageBox.Show(se.AdditionalInfo, se.Message);
				} catch (Exception ex) {
					MessageBox.Show("Unexpected error 2", ex.Message);
				}
				LoadIdentities();
			}
		}

		private void Connect(object sender, RoutedEventArgs e) {
			try {
				serviceClient.SetTunnelState(true);
				SetNotifyIcon("green");
				InitializeTimer(0);
				ConnectButton.Visibility = Visibility.Collapsed;
				DisconnectButton.Visibility = Visibility.Visible;
			} catch (ServiceException se) {
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Unexpected error 3", ex.Message);
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
		private void Disconnect(object sender, RoutedEventArgs e) {
			try {
				ConnectedTime.Content =  "00:00:00";
				_timer.Stop();
				serviceClient.SetTunnelState(false);
				SetNotifyIcon("white");
				ConnectButton.Visibility = Visibility.Visible;
				DisconnectButton.Visibility = Visibility.Collapsed;
			} catch (ServiceException se) {
				MessageBox.Show(se.AdditionalInfo, se.Message);
			} catch (Exception ex) {
				MessageBox.Show("Unexpected error 4", ex.Message);
			}
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

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			//
		}
	}
}
