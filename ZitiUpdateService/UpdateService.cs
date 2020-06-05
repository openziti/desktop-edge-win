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

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {

		private string _version = "";
		private bool _isNew = true;
		private int _majorVersion = 1;
		private string _versionUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/maven-metadata.xml";
		private string _serviceUrl = "https://netfoundry.jfrog.io/netfoundry/ziti-maven-snapshot/ziti-tunnel-win/amd64/windows/ziti-tunnel-win/${version}/ziti-tunnel-win-${version}.zip";
		private System.Timers.Timer _checkTimer = new System.Timers.Timer();
		private System.Timers.Timer _updateTimer = new System.Timers.Timer();
		private string _rootDirectory = "";
		private string _logDirectory = "";
		private bool _isJustStopped = true;

		ServiceController controller;
		public UpdateService() {
			InitializeComponent();
		}

		protected override void OnStart(string[] args) {
			_rootDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetFoundry");
			if (!Directory.Exists(_rootDirectory)) Directory.CreateDirectory(_rootDirectory);
			_logDirectory = Path.Combine(_rootDirectory, "Logs");
			if (!Directory.Exists(_logDirectory)) Directory.CreateDirectory(_logDirectory);
			Log("Setup Watchers");
			SetupServiceWatcher(); 
		}

		protected override void OnStop() {
			Log("Stopping update Service");
		}

		private void SetupServiceWatcher() {
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller==null) {
				Log("Get Installation");
				UpdateServiceFiles();
			} else {
				Log("Check Version");
				_isNew = false;
				if (File.Exists(Path.Combine(_rootDirectory, @"Service\Version.txt"))) {
					_version = File.ReadAllText(Path.Combine(_rootDirectory, @"Service\Version.txt"));
					Log("Version Found: " + _version);
					if (controller.Status==ServiceControllerStatus.Stopped) {
						StartZiti();
					} else {
						SetupWatcher();
					}
				} else {	
					UpdateServiceFiles();
				}
			}
		}

		private void SetupWatcher() {
			Log("Ziti Update Setting Up Watchers");
;			_checkTimer = new System.Timers.Timer();
			_checkTimer.Elapsed += CheckService;
			_checkTimer.Interval = 60000;
			_checkTimer.Enabled = true;
			_checkTimer.Start();
			_updateTimer = new System.Timers.Timer();
			_updateTimer.Elapsed += CheckUpdate;
			_updateTimer.Interval = 600000;
			_updateTimer.Enabled = true;
			_updateTimer.Start();
		}

		private void CheckUpdate(object sender, ElapsedEventArgs e) {
			var request = WebRequest.Create(_versionUrl) as HttpWebRequest;
			var response = request.GetResponse();
			Stream receiveStream = response.GetResponseStream();
			StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
			var result = readStream.ReadToEnd();
			XmlDocument xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(result);
			XmlNode node = xmlDoc.SelectSingleNode("metadata/versioning/release");
			string version = node.InnerText;
			Log("Version Checked: " + version+" on "+_version);
			if (version != _version) {
				StopZiti();
				UpdateServiceFiles();
			}
		}

		private void CheckService(object sender, ElapsedEventArgs e) {
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller.Status == ServiceControllerStatus.Stopped|| controller.Status == ServiceControllerStatus.StopPending) {
				if (_isJustStopped) {
					_isJustStopped = false;
					Log("Ziti Service has been stopped, update Network");
					RunScript();
				}
			} else {
				if (controller.Status==ServiceControllerStatus.Running) {
					_isJustStopped = true;
				}
			}
		}

		private void UpdateServiceFiles() {
			if (controller!=null) {
				controller.Stop();
			} else {
				GetFiles();
			}
		}

		private void GetFiles() {
			var request = WebRequest.Create(_versionUrl) as HttpWebRequest;
			var response = request.GetResponse();
			Stream receiveStream = response.GetResponseStream();
			StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);
			var result = readStream.ReadToEnd();
			XmlDocument xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(result);
			XmlNode node = xmlDoc.SelectSingleNode("metadata/versioning/release");
			string version = node.InnerText;
			Log("Got Version: " + version);

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
					StartZiti();
					Log("Installed " + installService.FileName + " " + installService.Arguments);

				} catch (Exception e) {
					Log("Cannot Install Service - " + e.ToString());
				}
			} else {
				StartZiti();
			}

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
			if (controller.Status != ServiceControllerStatus.Running) {
				try {
					Log("Starting Service");
					controller.Start();
					controller.WaitForStatus(ServiceControllerStatus.Running);
					SetupWatcher();
				} catch (Exception e) {
					Log("Cannot Start Service - " + e.ToString());
				}
			}
		}

		private void StopZiti() {
			controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
			if (controller.Status != ServiceControllerStatus.Stopped) {
				try {
					controller.Stop();
					controller.WaitForStatus(ServiceControllerStatus.Stopped);
				} catch (Exception e) {
					Log("Cannot Stop Service - " + e.ToString());
				}
			}
		}

		private void Log(string message) {
			File.AppendAllText(Path.Combine(_logDirectory, @"Log.log"), DateTime.Now.ToString()+" "+message);
			EventLog.WriteEntry("Ziti Update Service", message, EventLogEntryType.Information);
		}
	}
}
