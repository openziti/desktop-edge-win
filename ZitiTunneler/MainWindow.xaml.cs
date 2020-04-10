using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using Microsoft.Win32;
using ZitiTunneler.Models;

namespace ZitiTunneler {
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow:Window {

		private List<ZitiIdentity> identities = new List<ZitiIdentity>();
		private List<ZitiService> services = new List<ZitiService>();
		public MainWindow() {
			InitializeComponent();
		}

		private void MainWindow1_Loaded(object sender, RoutedEventArgs e) {
			var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
			this.Left = desktopWorkingArea.Right-this.Width-25;
			this.Top = desktopWorkingArea.Bottom-this.Height-25;
			CreateFakeData();
			LoadIdentities();
		}

		private void CreateFakeData() {
			services.Add(new ZitiService("Hush Services"));
			services.Add(new ZitiService("mPOS Service"));
			identities.Add(new ZitiIdentity("Jeremy-PC", "demo.ziti.controller.com:1280", true, services.ToArray()));
			services.Add(new ZitiService("eugenes secure hard drive"));
			identities.Add(new ZitiIdentity("Jeremy-iPaq", "ziti.netfoundry.io:1408", false, services.ToArray()));
			services.Add(new ZitiService("Red Tube Access"));
			services.Add(new ZitiService("Storage Services"));
			identities.Add(new ZitiIdentity("Hart-Mac", "ziti.supersecret.io:1408", true, services.ToArray()));
		}

		private void LoadIdentities() {
			ZitiIdentity[] ids = identities.ToArray();
			for (int i=0; i<ids.Length; i++) {
				IdentityItem id = new IdentityItem();
				id.Identity = ids[i];
				IdList.Children.Add(id);
			}
		}

		private void ShowMenu(object sender, MouseButtonEventArgs e) {
			MainMenu.Visibility = Visibility.Visible;
		}

		private void AddIdentity(object sender, MouseButtonEventArgs e) {
			OpenFileDialog jwtDialog = new OpenFileDialog();
			jwtDialog.DefaultExt = ".jwt";
			jwtDialog.Filter = "Ziti Identities (*.jwt)|*.jwt";
			if (jwtDialog.ShowDialog() == true) {
				string fileContent = File.ReadAllText(jwtDialog.FileName);
				// Clint!! AxedaBuddy - What to do with the jwt file?
			}
		}

		private void Connect(object sender, RoutedEventArgs e) {
			ConnectButton.Visibility = Visibility.Collapsed;
			DisconnectButton.Visibility = Visibility.Visible;
		}
		private void Disconnect(object sender, RoutedEventArgs e) {
			ConnectButton.Visibility = Visibility.Visible;
			DisconnectButton.Visibility = Visibility.Collapsed;
		}
	}
}
