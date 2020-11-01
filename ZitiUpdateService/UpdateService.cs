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

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private int checkTimerDefault = 60000;
		private int updateTimerDefault = 600000;

		private string _version = "";
		private bool _isNew = true;
		/// private int _majorVersion = 1;
		private string _versionUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml";
		private string _serviceUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${version}/ziti-tunnel-win-${version}.zip";
		private System.Timers.Timer _checkTimer = new System.Timers.Timer();
		private System.Timers.Timer _updateTimer = new System.Timers.Timer();
		private bool inUpdateCheck = false;
		private bool inServiceCheck = false;
		private string _rootDirectory = "";
		private string _logDirectory = "";
		private bool _isJustStopped = true;
		private string _versionType = "latest";

		ServiceController controller;
		public UpdateService() {
			InitializeComponent();

			Client svc = new Client();
			//svc.OnMetricsEvent += Svc_OnMetricsEvent;
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
				if (ConfigurationManager.AppSettings.Get("UpdateUrl") != null) _versionUrl = ConfigurationManager.AppSettings.Get("UpdateUrl");
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

			var serviceTimerInterval = ConfigurationManager.AppSettings.Get("ServiceTimer");
			var serviceInt = TimeSpan.Zero;
			if (!TimeSpan.TryParse(serviceTimerInterval, out serviceInt)) {
				serviceInt = new TimeSpan(0, 1, 0);
			}

			var updateTimerInterval = ConfigurationManager.AppSettings.Get("UpdateTimer");
			var upInt = TimeSpan.Zero;
			if (!TimeSpan.TryParse(updateTimerInterval, out upInt)) {
				upInt = new TimeSpan(0, 1, 0);
			}

			Logger.Info("Ziti Update Setting Up Watchers");

			_checkTimer = new System.Timers.Timer();
			_checkTimer.Elapsed += CheckService;
			_checkTimer.Interval = serviceInt.TotalMilliseconds;
			_checkTimer.Enabled = true;
			_checkTimer.Start();
			Logger.Info("Service Checker is running");

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
			try {
				var request = WebRequest.Create(_versionUrl) as HttpWebRequest;
				var response = request.GetResponse();
				Stream receiveStream = response.GetResponseStream();
				StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
				var result = readStream.ReadToEnd();
				XmlDocument xmlDoc = new XmlDocument();
				xmlDoc.LoadXml(result);
				XmlNode node = xmlDoc.SelectSingleNode("metadata/versioning/" + _versionType);
				string version = node.InnerText;
				if (version != _version) {
					Logger.Info("Version Checked: " + version + " on " + _version + " from " + _versionType);
					UpdateServiceFiles(version);
				}
			} catch {

            }
			inUpdateCheck = false;
		}

		private void CheckService(object sender, ElapsedEventArgs e) {
			if (inServiceCheck || inUpdateCheck) return;
			inServiceCheck = true; //simple semaphone
			try {
				controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
				if (controller == null) return; //means it's not installed or the user doesn't have privs

				if (controller.Status == ServiceControllerStatus.Stopped || controller.Status == ServiceControllerStatus.StopPending) {
					if (_isJustStopped) {
						_isJustStopped = false;
						Logger.Info("Ziti Service has been stopped, update Network");
						//should not be necessary in v0.0.10+ RunScript();
						StartZiti();
					}
				} else {
					if (controller.Status == ServiceControllerStatus.Running) {
						_isJustStopped = true;
					}
				}
			} catch {

			}
			inServiceCheck = false;
		}

		private void UpdateServiceFiles(string currentVersion) {
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

		private void RunScript() {
			string scriptText = "get-netipinterface|ForEach-Object { Set-DnsClientServerAddress-InterfaceIndex $_.ifIndex-ResetServerAddresses}";
			Logger.Info("Executing: " + scriptText);
			using (PowerShell PowerShellInstance = PowerShell.Create()) {
				PowerShellInstance.AddScript(scriptText);
				IAsyncResult result = PowerShellInstance.BeginInvoke();
				while (result.IsCompleted == false) {
					Thread.Sleep(1000);
				}
			}
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
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller != null && controller.Status != ServiceControllerStatus.Stopped) {
				try {
					controller.Stop();
					controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
				} catch (Exception e) {
					Logger.Info("Cannot Stop Service - " + e.ToString());
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

		private static void Svc_OnMetricsEvent(object sender, List<Identity> e) {
			foreach (Identity i in e) {
				Logger.Info("metrics!: " + i.Name);
			}
		}
	}
}
