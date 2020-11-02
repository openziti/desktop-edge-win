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
using NLog.Config;

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

		private Client svc = new Client();
		private bool running = false;

		ServiceController controller;
		public UpdateService() {
			InitializeComponent();

			Logger.Info("Initializing");
			svc.OnClientConnected += Svc_OnClientConnected;
			svc.OnTunnelStatusEvent += Svc_OnTunnelStatusEvent;
			svc.OnClientDisconnected += Svc_OnClientDisconnected;
			svc.OnShutdownEvent += Svc_OnShutdownEvent;
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
			if (!running) {
				running = true;
				Task.Run(() => {
					SetupServiceWatchers();
				});
			}
			Logger.Info("initialized and running");
		}

		protected override void OnStop() {
			Logger.Info("stopping update service");
		}

		private void SetupServiceWatchers() {

			var updateTimerInterval = ConfigurationManager.AppSettings.Get("UpdateTimer");
			var upInt = TimeSpan.Zero;
			if (!TimeSpan.TryParse(updateTimerInterval, out upInt)) {
				upInt = new TimeSpan(0, 1, 0);
			}

			_updateTimer = new System.Timers.Timer();
			_updateTimer.Elapsed += CheckUpdate;
			_updateTimer.Interval = upInt.TotalMilliseconds;
			_updateTimer.Enabled = true;
			_updateTimer.Start();
			Logger.Info("Version Checker is running");
			CheckUpdate(null, null); //check immediately

			try {
				svc.Connect();
			} catch {
				svc.Reconnect();
			}

			svc.WaitForConnection();
		}

		private void CheckUpdate(object sender, ElapsedEventArgs e) {
			if (inUpdateCheck) return;
			inUpdateCheck = true; //simple semaphone
			try {
				Logger.Debug("checking for update");
				var updateUrl = ConfigurationManager.AppSettings.Get("UpdateUrl");
				if (string.IsNullOrEmpty(updateUrl)) {
					updateUrl = "https://api.github.com/repos/openziti/desktop-edge-win/releases/latest";
				}
				IUpdateCheck check = new GithubCheck(updateUrl);
				//IUpdateCheck check = new FilesystemCheck();

				string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(); //fetch from ziti?
				Version installed = new Version(currentVersion);
				if (!check.IsUpdateAvailable(installed)) {
					Logger.Debug("update check complete. no update available");
					inUpdateCheck = false;
					return;
				}

				Logger.Info("update is available.");

				var curdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

				var updateFolder = $"{curdir}{Path.DirectorySeparatorChar}updates";
				Directory.CreateDirectory(updateFolder);

				Logger.Info("copying update package");
				string filename = check.FileName();

				if (check.AlreadyDownloaded(updateFolder, filename)) {
					Logger.Info("package has already been downloaded - moving to install phase");
				} else {
					Logger.Info("copying update package begins");
					check.CopyUpdatePackage(updateFolder, filename);
					Logger.Info("copying update package complete");
				}

				StopZiti();

				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				string fileDestination = Path.Combine(updateFolder, filename);
				Process.Start(fileDestination, "/passive");
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error has occurred");
			}
			inUpdateCheck = false;
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
