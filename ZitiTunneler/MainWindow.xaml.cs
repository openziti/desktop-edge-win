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
	public partial class MainWindow:Window
	{
		public System.Windows.Forms.NotifyIcon notifyIcon;
		private ServiceClient.Client serviceClient = null;

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

			SetNotifyIcon("white");

			InitializeComponent();
		}

		private void TargetNotifyIcon_Click(object sender, EventArgs e) {
			if (App.Current.MainWindow.WindowState==WindowState.Minimized) {
				App.Current.MainWindow.WindowState = WindowState.Normal;
				App.Current.MainWindow.BringIntoView();
				//this.Opacity = 1;
			} else {
				App.Current.MainWindow.WindowState = WindowState.Minimized;
				//this.Opacity = 0;
			}
		}

		private void MainWindow1_Loaded(object sender, RoutedEventArgs e) {
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			this.Left = desktopWorkingArea.Right-this.Width-25;
			this.Top = desktopWorkingArea.Bottom-this.Height-25;

			// add a new service client
			serviceClient = new ServiceClient.Client();
			Application.Current.Properties.Add("ServiceClient", serviceClient);
			Application.Current.Properties.Add("Identities", new List<ZitiIdentity>());
			LoadStatusFromService();
			LoadIdentities();
		}

		private void LoadStatusFromService() {
			ZitiTunnelStatus currentData = serviceClient.GetStatus();

			foreach(var id in currentData.Identities)
			{
				updateViewWithIdentity(id);
			}
		}

		private void updateViewWithIdentity(Identity id)
		{
			var zid = ZitiIdentity.FromClient(id);/*new ZitiIdentity()
			{
				Name = id.Name,
				ControllerUrl = id.Config.ztAPI,
				Fingerprint = id.FingerPrint,
				EnrollmentStatus = id.Status,
				Status = id.Status,
				IsEnabled = id.Active
			};
			if (id.Services != null)
			{
				foreach (var svc in id.Services)
				{
					var zsvc = new ZitiService(svc.Name, svc.HostName + ":" + svc.Port);
					zid.Services.Add(zsvc);
					services.Add(zsvc);
				}
			}*/
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
				catch(ServiceClient.ServiceException se)
				{
					MessageBox.Show(se.AdditionalInfo, se.Message);
				}
				catch(Exception ex)
				{
					MessageBox.Show("Unexpected error", ex.Message);
				}
			}
			LoadIdentities();
		}

		private void Connect(object sender, RoutedEventArgs e) {
			SetNotifyIcon("green");
			ConnectButton.Visibility = Visibility.Collapsed;
			DisconnectButton.Visibility = Visibility.Visible;

			try
			{
				serviceClient.SetTunnelState(true);
			}
			catch (ServiceClient.ServiceException se)
			{
				MessageBox.Show(se.AdditionalInfo, se.Message);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Unexpected error", ex.Message);
			}
		}
		private void Disconnect(object sender, RoutedEventArgs e) {
			SetNotifyIcon("white");
			ConnectButton.Visibility = Visibility.Visible;
			DisconnectButton.Visibility = Visibility.Collapsed;

			try
			{
				serviceClient.SetTunnelState(false);
			}
			catch (ServiceClient.ServiceException se)
			{
				MessageBox.Show(se.AdditionalInfo, se.Message);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Unexpected error", ex.Message);
			}
		}
	}
}
