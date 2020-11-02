using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System;
using System.Threading;
using System.Management.Automation;
using ZitiDesktopEdge.Models;
using System.Reflection;
using System.Web;
using System.Net.Mail;
using System.IO;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace ZitiDesktopEdge
{	
    /// <summary>
    /// Interaction logic for MainMenu.xaml
    /// </summary>
    public partial class MainMenu : UserControl {


		public delegate void AttachementChanged(bool attached);
		public event AttachementChanged OnAttachmentChange;
		public delegate void LogLevelChanged(string level);
		public event LogLevelChanged OnLogLevelChanged;
		public delegate void Detched(MouseButtonEventArgs e);
		public event Detched OnDetach;
		public string menuState = "Main";
		public string licenseData = "it's open source.";
		public string LogLevel = "";
		private string _updateUrl = "https://api.github.com/repos/openziti/desktop-edge-win/releases/latest";
		private string _downloadUrl = "";

		public MainMenu() {
            InitializeComponent();
			LicensesItems.Text = licenseData;
			CheckUpdates();
		}

		private void HideMenu(object sender, MouseButtonEventArgs e) {
			menuState = "Menu";
			UpdateState();
			MainMenuArea.Visibility = Visibility.Collapsed;
		}
		private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
			if (e.ChangedButton == MouseButton.Left) {
				OnDetach(e);
			}
		}

		private void CloseApp(object sender, MouseButtonEventArgs e) {
			Application.Current.Shutdown();
		}

		private void DoUpdate(object sender, MouseButtonEventArgs e) {
			System.Diagnostics.Process.Start(_downloadUrl);
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
		private void ShowUILogs(object sender, MouseButtonEventArgs e) {
			menuState = "UILogs";
			UpdateState();
		}
		private void SetLogLevel(object sender, MouseButtonEventArgs e) {
			menuState = "LogLevel";
			UpdateState();
		}

		private void CheckUpdates() {
			try
			{
				HttpWebRequest httpWebRequest = WebRequest.CreateHttp(_updateUrl);
				httpWebRequest.Method = "GET";
				httpWebRequest.ContentType = "application/json";
				httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.103 Safari/537.36";
				HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
				StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
				string result = streamReader.ReadToEnd();
				JObject json = JObject.Parse(result);
				string currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
				string serverVersion = json.Property("tag_name").Value.ToString() + ".0";

				Version installed = new Version(currentVersion);
				Version published = new Version(serverVersion);
				int compare = installed.CompareTo(published);
				if (compare < 0)
				{
					UpdateAvailable.Content = "An Upgrade is available, click to download";
					UpdateAvailable.Visibility = Visibility.Visible;
				}
				else if (compare > 0)
				{
					UpdateAvailable.Content = "Your version is newer than the released version";
					UpdateAvailable.Visibility = Visibility.Visible;
				}
				JArray assets = JArray.Parse(json.Property("assets").Value.ToString());
				foreach (JObject asset in assets.Children<JObject>())
				{
					_downloadUrl = asset.Property("browser_download_url").Value.ToString();
					break;
				}
			} catch(Exception ex)
			{
				UpdateAvailable.Content = "An exception occurred while performing upgrade check";
				Debug.WriteLine("Error when checking for version: " + ex.Message);
				UpdateAvailable.Visibility = Visibility.Visible;
			}
		}

		private void UpdateState() {
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

			if (menuState == "About") {
				MenuTitle.Content = "About";
				AboutItemsArea.Visibility = Visibility.Visible;
				AboutItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;

				string version = "";
				try {
					ServiceClient.TunnelStatus s = (ServiceClient.TunnelStatus)Application.Current.Properties["CurrentTunnelStatus"];
					version = $"{s.ServiceVersion.Version}@{s.ServiceVersion.Revision}";
				} catch (Exception e) {
#if DEBUG
					Debug.WriteLine(e.ToString());
#endif
				}

				// Interface Version
				VersionInfo.Content = "App: " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString()+" Service: "+ version;

			} else if (menuState=="Advanced") {
				MenuTitle.Content = "Advanced Settings";
				AdvancedItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Licenses") {
				MenuTitle.Content = "Third Party Licenses";
				LicensesItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Logs") {
				ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
				MenuTitle.Content = "Service Logs";
				LogsItems.Text = client.GetLogs();
				LogsItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState == "UILogs") {
				MenuTitle.Content = "Application Logs";
				LogsItems.Text = UILog.GetLogs();
				LogsItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState == "LogLevel") {
				ResetLevels();

				MenuTitle.Content = "Set Log Level";
				LogLevelItems.Visibility = Visibility.Visible;
				BackArrow.Visibility = Visibility.Visible;
			} else if (menuState=="Config") {
				MenuTitle.Content = "Tunnel Configuration";
				ConfigItems.Visibility = Visibility.Visible;
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
			if (menuState=="Config"||menuState=="Logs"||menuState=="UILogs") {
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
			ServiceClient.Client client = (ServiceClient.Client)Application.Current.Properties["ServiceClient"];
			var mailMessage = new MailMessage();
			mailMessage.From = new MailAddress("ziti-support@netfoundry.io");
			mailMessage.Subject = "Ziti Support";
			mailMessage.IsBodyHtml = true;
			mailMessage.Body = "";

			string timestamp = DateTime.Now.ToFileTime().ToString();
			string serviceLogTempFile = Path.Combine(Path.GetTempPath(), timestamp+"-Ziti-Service.log");
			using (StreamWriter sw = new StreamWriter(serviceLogTempFile)) {
				sw.WriteLine(client.GetLogs());
			}

			string uiLogTempFile = Path.Combine(Path.GetTempPath(), timestamp+"-Ziti-Application.log");
			using (StreamWriter sw = new StreamWriter(uiLogTempFile)) {
				sw.WriteLine(UILog.GetLogs());
			}

			mailMessage.Attachments.Add(new Attachment(serviceLogTempFile));
			mailMessage.Attachments.Add(new Attachment(uiLogTempFile));

			string emlFile = Path.Combine(Path.GetTempPath(), timestamp+"-ziti.eml");

			using (var filestream = File.Open(emlFile, FileMode.Create)) {
				var binaryWriter = new BinaryWriter(filestream);
				binaryWriter.Write(System.Text.Encoding.UTF8.GetBytes("X-Unsent: 1" + Environment.NewLine));
				var assembly = typeof(SmtpClient).Assembly;
				var mailWriterType = assembly.GetType("System.Net.Mail.MailWriter");
				var mailWriterContructor = mailWriterType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(Stream) }, null);
				var mailWriter = mailWriterContructor.Invoke(new object[] { filestream });
				var sendMethod = typeof(MailMessage).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic);
				sendMethod.Invoke(mailMessage, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { mailWriter, true, true }, null);
				var closeMethod = mailWriter.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic);
				closeMethod.Invoke(mailWriter, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { }, null);
			}

			Process.Start(emlFile);



			//string body = HttpUtility.UrlEncode("\n\nService Logs\n\n" + client.GetLogs());// + "\n\nApplication Logs\n\n" + UILog.GetLogs());
			//Process.Start(new ProcessStartInfo("mailto:ziti-support@netfoundry.io?subject=Ziti%20Support&body="+body) { UseShellExecute = true });
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
			LogFatal.IsSelected = false;
			LogWarn.IsSelected = false;
			LogTrace.IsSelected = false;
			if (this.LogLevel == "verbose") LogVerbose.IsSelected = true;
			else if (this.LogLevel == "debug") LogDebug.IsSelected = true;
			else if (this.LogLevel == "info") LogInfo.IsSelected = true;
			else if (this.LogLevel == "error") LogError.IsSelected = true;
			else if (this.LogLevel == "fatal") LogFatal.IsSelected = true;
			else if (this.LogLevel == "warn") LogWarn.IsSelected = true;
			else if (this.LogLevel == "trace") LogTrace.IsSelected = true;
		}

		private void SetLevel(object sender, MouseButtonEventArgs e) {
			SubOptionItem item = (SubOptionItem)sender;
			this.LogLevel = item.Label.ToLower();
			if (OnLogLevelChanged != null) {
				OnLogLevelChanged(this.LogLevel);
			}
			ResetLevels();
		}
	}
}
