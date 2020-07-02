using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
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
using System.Collections.Specialized;

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {

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
		}

		public void Debug() {
			OnStart(null);
		}

		protected override void OnStart(string[] args) {
			try {
				if (ConfigurationManager.AppSettings.Get("UpdateUrl") != null) _versionUrl = ConfigurationManager.AppSettings.Get("UpdateUrl");
				if (ConfigurationManager.AppSettings.Get("Version") != null) _versionType = ConfigurationManager.AppSettings.Get("Version");
			} catch (Exception e) {
				Log(e.ToString());
			}
			_rootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetFoundry");
			if (!Directory.Exists(_rootDirectory)) Directory.CreateDirectory(_rootDirectory);
			_logDirectory = Path.Combine(_rootDirectory, "Logs");
			if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
			Log("Setup Watchers");
			SetupServiceWatchers(); 
		}

		protected override void OnStop() {
			Log("Stopping update Service");
		}

		private void SetupServiceWatchers() {
			Log("Setting Up Watchers");

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

			Log("Ziti Update Setting Up Watchers");

			_checkTimer = new System.Timers.Timer();
			_checkTimer.Elapsed += CheckService;
			_checkTimer.Interval = serviceInt.TotalMilliseconds;
			_checkTimer.Enabled = true;
			_checkTimer.Start();
			Log("Service Checker is running");

			_updateTimer = new System.Timers.Timer();
			_updateTimer.Elapsed += CheckUpdate;
			_updateTimer.Interval = upInt.TotalMilliseconds;
			_updateTimer.Enabled = true;
			_updateTimer.Start();
			Log("Version Checker is running");
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
					Log("Version Checked: " + version + " on " + _version + " from " + _versionType);
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
						Log("Ziti Service has been stopped, update Network");
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
			Log("Got Version: " + version+" from "+_versionType);

			if (_version.Trim() == currentVersion.Trim()) {
				_version = currentVersion;
				Log("Version local is the same as the version remote: " + version);
				return;
			}

			StopZiti();
			string remoteService = _serviceUrl.Replace("${version}", version);

			if (!Directory.Exists(Path.Combine(_rootDirectory, "Service"))) Directory.CreateDirectory(Path.Combine(_rootDirectory, "Service"));
			string[] files = Directory.GetFiles(Path.Combine(_rootDirectory, "Service"));
			foreach (string file in files) {
				try {
					Log("Delete: " + file);
					File.Delete(file);
				} catch (Exception e) {
					EventLog.WriteEntry("Ziti", e.ToString(), EventLogEntryType.Error);
				}
			} 
			WebClient webClient = new WebClient();
			Log("Get From: "+remoteService);
			webClient.DownloadFile(remoteService, Path.Combine(_rootDirectory, "Service") + @"\windows-tunneler.zip");
			Log("Zip Downloaded");
			ZipFile.ExtractToDirectory(Path.Combine(_rootDirectory, "Service") + @"\windows-tunneler.zip", Path.Combine(_rootDirectory, "Service"));

			Log("Zip UnZipped");
			File.WriteAllText(Path.Combine(_rootDirectory, @"Service\Version.txt"), version);
			_version = version;

			if (_isNew) {
				Log("Installing Service " + _version);
				ProcessStartInfo installService = new ProcessStartInfo();
				installService.CreateNoWindow = true;
				installService.UseShellExecute = true;
				installService.FileName = Path.Combine(_rootDirectory, "Service") + @"\ziti-tunnel.exe";
				installService.WindowStyle = ProcessWindowStyle.Hidden;
				installService.Arguments = "install";
				Log("Install Using " + installService.FileName + " " + installService.Arguments);
				try {
					Process exeProcess = Process.Start(installService);
					exeProcess.WaitForExit();
					Log("Installed " + installService.FileName + " " + installService.Arguments);

				} catch (Exception e) {
					Log("Cannot Install Service - " + e.ToString());
				}
			} else {
				// nothing to do
			}
			StartZiti();
		}

		private void RunScript() {
			string scriptText = "get-netipinterface|ForEach-Object { Set-DnsClientServerAddress-InterfaceIndex $_.ifIndex-ResetServerAddresses}";
			Log("Executing: " + scriptText);
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
			if (controller != null && controller.Status != ServiceControllerStatus.Running) {
				try {
					Log("Starting Service");
					controller.Start();
					controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
					SetupServiceWatchers();
				} catch (Exception e) {
					Log("Cannot Start Service - " + e.ToString());
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
					Log("Cannot Stop Service - " + e.ToString());
				}
			}
		}

		object logLock = new object();
		private void Log(string message) {
			lock (logLock) {
				File.AppendAllText(Path.Combine(_logDirectory, @"Log.log"), DateTime.Now.ToString() + " " + message + '\n');
				EventLog.WriteEntry("Ziti Update Service", message, EventLogEntryType.Information);
			}
		}
	}
}
