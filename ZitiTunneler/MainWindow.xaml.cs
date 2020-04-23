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

		private List<ZitiIdentity> identities
		{
			get
			{
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
			this.Left = desktopWorkingArea.Right-this.Width-25;
			this.Top = desktopWorkingArea.Bottom-this.Height-25;

			// add a new service client
			serviceClient = new Client();
			Application.Current.Properties.Add("ServiceClient", serviceClient);
			Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
			try
			{
				LoadStatusFromService();
				MessageBox.Show("identites are returned from the server. Any that were 'on' will have services. any off won't. Update the toggles to show if they are on or off");
			}
			catch(Exception ex)
			{
				//probably some kind of problem with the service...
				MessageBox.Show("oh my goodness - problem with the service. Almost certainly means the service is NOT RUNNING... Jeremy make this pretty.\n" + ex.Message);
			}
			LoadIdentities();
		}

		private void LoadStatusFromService() {
			ZitiTunnelStatus status = serviceClient.GetStatus();
			if (status != null)
			{
				Application.Current.Properties.Add("ip", status?.IpInfo?.Ip);
				Application.Current.Properties.Add("subnet", status?.IpInfo?.Subnet);
				Application.Current.Properties.Add("mtu", status?.IpInfo?.MTU);
				Application.Current.Properties.Add("dns", status?.IpInfo?.DNS);

				foreach (var id in status.Identities)
				{
					updateViewWithIdentity(id);
				}
			}
			else
			{
				MessageBox.Show("could not get status - make this pretty Jeremy");
			}
		}

		private void updateViewWithIdentity(Identity id)
		{
			var zid = ZitiIdentity.FromClient(id);
			identities.Add(zid);
		}
		private void SetNotifyIcon(string iconPrefix) {
			System.IO.Stream iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,/Assets/Images/ziti-"+iconPrefix+".ico")).Stream;
			notifyIcon.Icon = new System.Drawing.Icon(iconStream);
		}

		private void LoadIdentities() {
			IdList.Children.Clear();
			ZitiIdentity[] ids = identities.ToArray();
			for (int i=0; i<ids.Length; i++) {
				IdentityItem id = new IdentityItem();
				id.Identity = ids[i];
				id.OnClick += OpenIdentity;
				IdList.Children.Add(id);
			}
			UIMain.Height = 480+(identities.Count*60);
			BgColor.Height = 480+(identities.Count*60);
			FormGrowAnimation.To = UIMain.Height;
			FormGrowOutAnimation.From = BgColor.Height;
			App.Current.MainWindow.Height = 490+(identities.Count*60);
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			this.Left = desktopWorkingArea.Right-this.Width-25;
			this.Top = desktopWorkingArea.Bottom-this.Height-25;
		}

		private void OpenIdentity(ZitiIdentity identity) {
			IdentityMenu.Identity = identity;
			IdentityMenu.Visibility = Visibility.Visible;
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

				// Jeremy!! AxedaBuddy - DUN!
				try
				{
					Identity createdId = serviceClient.AddIdentity(System.IO.Path.GetFileName(jwtDialog.FileName), false, fileContent);
					if (createdId != null)
					{
						identities.Add(ZitiIdentity.FromClient(createdId));
						MessageBox.Show("New identity added with fingerprint: " + createdId.FingerPrint);
						updateViewWithIdentity(createdId);
					}
					else
					{
						// Jeremy buddy - error popup here
						MessageBox.Show("created id was null - wtf jeremy. your fault");
					}
				}
				catch (ServiceException se)
				{
					MessageBox.Show(se.AdditionalInfo, se.Message);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Unexpected error 2", ex.Message);
				}
				LoadIdentities();
			}
		}

		private void Connect(object sender, RoutedEventArgs e) {
			SetNotifyIcon("green");
			_startDate = DateTime.Now;
			_timer = new System.Windows.Forms.Timer();
			_timer.Interval = 1000;
			_timer.Tick += OnTimedEvent;
			_timer.Enabled = true;
			_timer.Start();
			ConnectButton.Visibility = Visibility.Collapsed;
			DisconnectButton.Visibility = Visibility.Visible;

			try
			{
				serviceClient.SetTunnelState(true);
			}
			catch (ServiceException se)
			{
				MessageBox.Show(se.AdditionalInfo, se.Message);
			}
			catch (Exception ex)
			{
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

		private void Disconnect(object sender, RoutedEventArgs e) {
			_timer.Stop();
			SetNotifyIcon("white");
			ConnectButton.Visibility = Visibility.Visible;
			DisconnectButton.Visibility = Visibility.Collapsed;

			try
			{
				serviceClient.SetTunnelState(false);
			}
			catch (ServiceException se)
			{
				MessageBox.Show(se.AdditionalInfo, se.Message);
			}
			catch (Exception ex)
			{
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
