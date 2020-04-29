using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.IO;
using ZitiTunneler.Models;

using ZitiTunneler.ServiceClient;


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
		private int _bottom = -10;

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

			SetNotifyIcon("white");

			InitializeComponent();
		}
		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (!_isAttached&&e.ChangedButton == MouseButton.Left) this.DragMove();
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
			this.Left = desktopWorkingArea.Right-this.Width-_right;
			this.Top = desktopWorkingArea.Bottom-this.Height-_bottom;

			// add a new service client
			serviceClient = new Client();
			Application.Current.Properties.Add("ServiceClient", serviceClient);
			Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
			MainMenu.OnAttachmentChange += AttachmentChanged;
			try {
				LoadStatusFromService();
				// MessageBox.Show("identites are returned from the server. Any that were 'on' will have services. any off won't. Update the toggles to show if they are on or off");
			} catch(Exception ex) {
				//probably some kind of problem with the service...
				// MessageBox.Show("oh my goodness - problem with the service. Almost certainly means the service is NOT RUNNING... Jeremy make this pretty.\n" + ex.Message);
			}
			LoadIdentities();
			IdentityMenu.OnForgot += IdentityForgotten;
		}

		private void IdentityForgotten(ZitiIdentity forgotten) {
			ZitiTunnelStatus status = serviceClient.GetStatus();
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
			ZitiTunnelStatus status = serviceClient.GetStatus();
			if (status != null) {
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
			} else {
				MessageBox.Show("could not get status - make this pretty Jeremy");
			}
		}

		private void updateViewWithIdentity(Identity id) {
			var zid = ZitiIdentity.FromClient(id);
			identities.Add(zid);
			LoadIdentities();
		}
		private void SetNotifyIcon(string iconPrefix) {
			System.IO.Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,/Assets/Images/ziti-"+iconPrefix+".ico")).Stream;
			notifyIcon.Icon = new System.Drawing.Icon(iconStream);
		}

		private void LoadIdentities() {
			IdList.Children.Clear();
			ZitiIdentity[] ids = identities.ToArray();
			this.Height = 460+(ids.Length*60);
			for (int i=0; i<ids.Length; i++) {
				IdentityItem id = new IdentityItem();
				id.Identity = ids[i];
				id.OnClick += OpenIdentity;
				IdList.Children.Add(id);
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
	}
}
