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
using System.Diagnostics;
using System.Windows.Navigation;

namespace ZitiTunneler
{	
    /// <summary>
    /// Interaction logic for MainMenu.xaml
    /// </summary>
    public partial class MainMenu : UserControl {

		public string menuState = "Main";

		public string licenseData = "it's open source - someone motivated could track this down. we'll add this some day i'm sure";

		public MainMenu() {
            InitializeComponent();
			LicensesItems.Text = licenseData;
		}

		private void HideMenu(object sender, MouseButtonEventArgs e) {
			menuState = "Menu";
			UpdateState();
			this.Visibility = Visibility.Collapsed;
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

		private void UpdateState() {
			MainItems.Visibility = Visibility.Collapsed;
			AboutItems.Visibility = Visibility.Collapsed;
			MainItemsButton.Visibility = Visibility.Collapsed;
			BackArrow.Visibility = Visibility.Collapsed;
			AdvancedItems.Visibility = Visibility.Collapsed;
			LicensesItems.Visibility = Visibility.Collapsed;
			LogsItems.Visibility = Visibility.Collapsed;
			ConfigSaveButton.Visibility = Visibility.Collapsed;
			ConfigItems.Visibility = Visibility.Collapsed;


			if (menuState=="About") {
				MenuTitle.Content = "About";
				AboutItemsButton.Visibility = Visibility.Visible;
				AboutItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Advanced") {
				MenuTitle.Content = "Advanced Settings";
				AdvancedItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Licenses") {
				MenuTitle.Content = "Third Party Licenses";
				LicensesItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Logs")
			{
				ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
				MenuTitle.Content = "Application Logs";
				LogsItems.Text = client.GetLogs();
				LogsItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Config") {
				MenuTitle.Content = "Tunnel Configuration";
				ConfigItems.Visibility = Visibility.Visible;
				ConfigSaveButton.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
				
				ConfigIp.Value = Application.Current.Properties["ip"]?.ToString();
				ConfigSubnet.Value = Application.Current.Properties["subnet"]?.ToString();
				ConfigMtu.Value = Application.Current.Properties["mtu"]?.ToString();
				ConfigDns.Value = Application.Current.Properties["dns"]?.ToString();
			} else {
				MenuTitle.Content = "Main Menu";
				MainItems.Visibility = Visibility.Visible;
				MainItemsButton.Visibility = Visibility.Visible;
			}
		}

		private void GoBack(object sender, MouseButtonEventArgs e) {
			if (menuState=="Config"||menuState=="Logs") {
				menuState = "Advanced";
			} else if (menuState=="Licenses") {
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
		private void ShowFeedback(object sender, MouseButtonEventArgs e) {
			Process.Start(new ProcessStartInfo("mailto:support@netfoundry.io") { UseShellExecute = true });
		}
		private void ShowSupport(object sender, MouseButtonEventArgs e) {
			Process.Start(new ProcessStartInfo("https://support.netfoundry.io") { UseShellExecute = true });
		}
		private void DetachWindow(object sender, MouseButtonEventArgs e) {

		}

		private void SaveConfig(object sender, RoutedEventArgs e) {
			// Jeremy - this can just be removed - saving the config is not important to this client
		}
	}
}
