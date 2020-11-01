using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Timers;
using System.Management.Automation;
using System.Xml;
using System.Configuration;
using System.Threading.Tasks;

using ZitiDesktopEdge.ServiceClient;
using NLog;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private string _version = "";
		private bool _isNew = true;
	
		private System.Timers.Timer _updateTimer = new System.Timers.Timer();
		private bool inUpdateCheck = false;
		private string _rootDirectory = "";
		private string _logDirectory = "";
		private string _versionType = "latest";

		ServiceController controller;
		public UpdateService() {
			InitializeComponent();

			Client svc = new Client();
			svc.OnClientConnected += Svc_OnClientConnected;
			svc.OnTunnelStatusEvent += Svc_OnTunnelStatusEvent;
			svc.OnClientDisconnected += Svc_OnClientDisconnected;
			svc.OnShutdownEvent += Svc_OnShutdownEvent;
			try {
				svc.Connect();
			} catch {
				svc.Reconnect();
			}

			svc.WaitForConnection();
		}

		public void Debug() {
			OnStart(null);
		}

		protected override void OnStart(string[] args) {
			try {
				if (ConfigurationManager.AppSettings.Get("Version") != null) _versionType = ConfigurationManager.AppSettings.Get("Version");
			} catch (Exception e) {
				Logger.Info(e.ToString());
			}
			_rootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenZiti");
			if (!Directory.Exists(_rootDirectory)) Directory.CreateDirectory(_rootDirectory);
			_logDirectory = Path.Combine(_rootDirectory, "Logs");
			if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
			Logger.Info("Setup Watchers");
			SetupServiceWatchers(); 
		}

		protected override void OnStop() {
			Logger.Info("Stopping update Service");
		}

		private void SetupServiceWatchers() {
			Logger.Info("Setting Up Watchers");

			var updateTimerInterval = ConfigurationManager.AppSettings.Get("UpdateTimer");
			var upInt = TimeSpan.Zero;
			if (!TimeSpan.TryParse(updateTimerInterval, out upInt)) {
				upInt = new TimeSpan(0, 1, 0);
			}

			Logger.Info("Ziti Update Setting Up Watchers");

			_updateTimer = new System.Timers.Timer();
			_updateTimer.Elapsed += CheckUpdate;
			_updateTimer.Interval = upInt.TotalMilliseconds;
			_updateTimer.Enabled = true;
			_updateTimer.Start();
			Logger.Info("Version Checker is running");
			CheckUpdate(null, null); //check immediately
		}

		private void CheckUpdate(object sender, ElapsedEventArgs e) {
			if (inUpdateCheck) return;
			inUpdateCheck = true; //simple semaphone

			var updateUrl = ConfigurationManager.AppSettings.Get("UpdateUrl");
			if (string.IsNullOrEmpty(updateUrl)) {
				updateUrl = "https://api.github.com/repos/openziti/desktop-edge-win/releases/latest";
			}

			try {/*
				HttpWebRequest httpWebRequest = WebRequest.CreateHttp(updateUrl);
				httpWebRequest.Method = "GET";
				httpWebRequest.ContentType = "application/json";
				httpWebRequest.UserAgent = "OpenZiti UpdateService";
				HttpWebResponse httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
				StreamReader streamReader = new StreamReader(httpResponse.GetResponseStream());
				string result = streamReader.ReadToEnd();
				JObject json = JObject.Parse(result);
				string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
				string serverVersion = json.Property("tag_name").Value.ToString() + ".0";

				Version installed = new Version(currentVersion);
				Version published = new Version(serverVersion);
				int compare = installed.CompareTo(published);
				if (compare < 0) {
					Logger.Info("an upgrade is available. starting update process.");
				} else if (compare > 0) {
					Logger.Info("the version installed is newer than the released version");
					return;
				}
				JArray assets = JArray.Parse(json.Property("assets").Value.ToString());
				string downloadUrl = null;
				foreach (JObject asset in assets.Children<JObject>()) {
					downloadUrl = asset.Property("browser_download_url").Value.ToString();
					break;
				}

				if(downloadUrl == null) {
					Logger.Error("DOWNLOAD URL not found at: {0}", updateUrl);
					return;
				}
				*/
				var curdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

				var updateFolder = $"{curdir}{Path.DirectorySeparatorChar}updates";
				Directory.CreateDirectory(updateFolder);

				var downloadUrl = "https://github.com/openziti/desktop-edge-win/releases/download/1.2.12/Ziti.Desktop.Edge.Client-1.2.13.exe";
				string filename = downloadUrl.Substring(downloadUrl.LastIndexOf('/') + 1);

				if (string.IsNullOrEmpty(filename)) {
					Logger.Warn("filename not found from web request. using generic filename {0}", "Ziti.Desktop.Edge.Client.exe");
				}

				string fileDestination = Path.Combine(updateFolder, filename);
				Logger.Info("Checking to see if the file has been downloaded previously");
                if (File.Exists(fileDestination)) {
					Logger.Info("file already exists at {0}. not downloading again", fileDestination);
                } else {
					Logger.Info("update found. downloading update from {0} to {1}", downloadUrl, fileDestination);
					WebClient myWebClient = new WebClient();
					myWebClient.DownloadFile(downloadUrl, fileDestination);
					Logger.Info("update downloaded");
				}

				StopZiti();

				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error has occurred");
            }
			inUpdateCheck = false;
		}

		private void UpdateServiceFiles(string currentVersion, string _versionUrl, string _serviceUrl) {
			var request = WebRequest.Create(_versionUrl) as HttpWebRequest;
			var response = request.GetResponse();
			Stream receiveStream = response.GetResponseStream();
			StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
			var result = readStream.ReadToEnd();
			XmlDocument xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(result);
			XmlNode node = xmlDoc.SelectSingleNode("metadata/versioning/"+_versionType);
			string version = node.InnerText;
			Logger.Info("Got Version: " + version+" from "+_versionType);

			if (_version.Trim() == currentVersion.Trim()) {
				_version = currentVersion;
				Logger.Info("Version local is the same as the version remote: " + version);
				return;
			}

			StopZiti();
			string remoteService = _serviceUrl.Replace("${version}", version);

			if (!Directory.Exists(Path.Combine(_rootDirectory, "Service"))) Directory.CreateDirectory(Path.Combine(_rootDirectory, "Service"));
			string[] files = Directory.GetFiles(Path.Combine(_rootDirectory, "Service"));
			foreach (string file in files) {
				try {
					Logger.Info("Delete: " + file);
					File.Delete(file);
				} catch (Exception e) {
					EventLog.WriteEntry("Ziti", e.ToString(), EventLogEntryType.Error);
				}
			} 
			WebClient webClient = new WebClient();
			Logger.Info("Get From: "+remoteService);
			webClient.DownloadFile(remoteService, Path.Combine(_rootDirectory, "Service") + @"\windows-tunneler.zip");
			Logger.Info("Zip Downloaded");
			ZipFile.ExtractToDirectory(Path.Combine(_rootDirectory, "Service") + @"\windows-tunneler.zip", Path.Combine(_rootDirectory, "Service"));

			Logger.Info("Zip UnZipped");
			File.WriteAllText(Path.Combine(_rootDirectory, @"Service\Version.txt"), version);
			_version = version;

			if (_isNew) {
				Logger.Info("Installing Service " + _version);
				ProcessStartInfo installService = new ProcessStartInfo();
				installService.CreateNoWindow = true;
				installService.UseShellExecute = true;
				installService.FileName = Path.Combine(_rootDirectory, "Service") + @"\ziti-tunnel.exe";
				installService.WindowStyle = ProcessWindowStyle.Hidden;
				installService.Arguments = "install";
				Logger.Info("Install Using " + installService.FileName + " " + installService.Arguments);
				try {
					Process exeProcess = Process.Start(installService);
					exeProcess.WaitForExit();
					Logger.Info("Installed " + installService.FileName + " " + installService.Arguments);

				} catch (Exception e) {
					Logger.Info("Cannot Install Service - " + e.ToString());
				}
			} else {
				// nothing to do
			}
			StartZiti();
		}

		private void StartZiti() {
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller != null && controller.Status != ServiceControllerStatus.Running && controller.Status != ServiceControllerStatus.StartPending && controller.Status != ServiceControllerStatus.ContinuePending) {
				try {
					Logger.Info("Starting Service");
					controller.Start();
					controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
					SetupServiceWatchers();
				} catch (Exception e) {
					Logger.Info("Cannot Start Service - " + e.ToString());
				}
			}
		}

		private void StopZiti() {
			Logger.Info("Stopping the ziti service...");
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller != null && controller.Status != ServiceControllerStatus.Stopped) {
				try {
					controller.Stop();
					controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
				} catch (Exception e) {
					Logger.Error(e, "Timout while trying to stop service!");
				}
			}
		}

		private static void Svc_OnShutdownEvent(object sender, StatusEvent e) {
			Logger.Info("the service is shutting down normally...");
		}

		private static void Svc_OnTunnelStatusEvent(object sender, TunnelStatusEvent e) {
			string dns = e?.Status?.IpInfo?.DNS;
			string version = e?.Status?.ServiceVersion.Version;
			string op = e?.Op;
			Logger.Info($"Operation {op}. running dns: {dns} at version {version}");
		}

		private static void Svc_OnClientConnected(object sender, object e) {
			Logger.Info("successfully connected to service");
		}

		private static void Svc_OnClientDisconnected(object sender, object e) {

			Client svc = (Client)sender;
			if (svc.CleanShutdown) {
				//then this is fine and expected - the service is shutting down
				Logger.Info("client disconnected due to clean service shutdown");
			} else {
				Logger.Error("SERVICE IS DOWN and did not exit cleanly. initiating DNS cleanup");
				//EnumerateDNS();
				var ps = System.Management.Automation.PowerShell.Create();
				string script = "Get-NetIPInterface | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }";
				ps.Commands.AddScript(script);
				Logger.Info("No longer connected to the service. Resetting the network by executing reset script.");
				Task.Delay(1000).Wait();
				ps.Invoke();
				Logger.Info("Reset script executed.");
				//EnumerateDNS();
			}
		}

		private static void EnumerateDNS() {
			var ps = System.Management.Automation.PowerShell.Create();
			ps.AddScript("Get-DnsClientServerAddress");
			var results = ps.Invoke();

			using (StringWriter sw = new StringWriter()) {
				foreach (var r in results) {
					string name = (string)r.Properties["InterfaceAlias"].Value;
					string[] dnses = (string[])r.Properties["ServerAddresses"].Value;
					sw.WriteLine($"Interface: {name}. DNS: {string.Join(",", dnses)}");
				}
				Logger.Info("DNS RESULTS:\n{0}", sw.ToString());
			}
		}
	}
}
