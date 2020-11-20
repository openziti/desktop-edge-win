using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.IO;
using System.Timers;
using System.Configuration;
using System.Threading.Tasks;

using ZitiDesktopEdge.DataStructures;
using NLog;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using ZitiDesktopEdge.ServiceClient;
using ZitiDesktopEdge.Server;

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {
		private static string[] expected_hashes = new string[] { "39636E9F5E80308DE370C914CE8112876ECF4E0C" };
		private static string[] expected_subject = new string[] { @"CN=""NetFoundry, Inc."", O=""NetFoundry, Inc."", L=Herndon, S=Virginia, C=US" };

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private Timer _updateTimer = new Timer();
		private bool inUpdateCheck = false;
		private string _rootDirectory = "";
		private string _logDirectory = "";
		private string _versionType = "latest";

		private DataClient svc = new DataClient();
		private bool running = false;

		ServiceController controller;
		ZitiDesktopEdge.Server.IPCServer svr = new ZitiDesktopEdge.Server.IPCServer();
		Task ipcServer = null;
		Task eventServer = null;

		public UpdateService() {
			InitializeComponent();

			Logger.Info("Initializing");
			svc.OnClientConnected += Svc_OnClientConnected;
			svc.OnTunnelStatusEvent += Svc_OnTunnelStatusEvent;
			svc.OnClientDisconnected += Svc_OnClientDisconnected;
			svc.OnShutdownEvent += Svc_OnShutdownEvent;
		}

		public void Debug() {
			OnStart(new string[] { "FilesystemCheck" });
		}

		protected override void OnStart(string[] args) {
			Logger.Info("ziti-monitor service is starting");
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
					SetupServiceWatchers(args);
				});
			}

			ipcServer = svr.startIpcServer();
			eventServer = svr.startEventsServer();

			Logger.Info("ziti-monitor service is initialized and running");
		}

		public void WaitForCompletion() {
			Task.WaitAll(ipcServer, eventServer);
		}

		protected override void OnStop() {
			Logger.Info("ziti-monitor service is stopping");
		}

		private void SetupServiceWatchers(string[] args) {

			var updateTimerInterval = ConfigurationManager.AppSettings.Get("UpdateTimer");
			var upInt = TimeSpan.Zero;
			if (!TimeSpan.TryParse(updateTimerInterval, out upInt)) {
				upInt = new TimeSpan(0, 1, 0);
			}

			_updateTimer = new Timer();
			_updateTimer.Elapsed += CheckUpdate;
			_updateTimer.Interval = upInt.TotalMilliseconds;
			_updateTimer.Enabled = true;
			_updateTimer.Start();
			Logger.Info("Version Checker is running");
			CheckUpdate(args, null); //check immediately

			try {
				svc.ConnectAsync().Wait();
			} catch {
				svc.Reconnect();
			}

			svc.WaitForConnectionAsync().Wait();
		}

		private void CheckUpdate(object sender, ElapsedEventArgs e) {
			if (inUpdateCheck) return;
			inUpdateCheck = true; //simple semaphone
			try {
				Logger.Debug("checking for update");
				string updateUrl = "https://api.github.com/repos/openziti/desktop-edge-win/releases/latest"; //hardcoded on purpose
				string[] senderAsArgs = (string[])sender;
				IUpdateCheck check = null;
				if (sender == null || senderAsArgs.Length < 1 || !senderAsArgs[0].Equals("FilesystemCheck")) {
					check = new GithubCheck(updateUrl);
				} else {
					check = new FilesystemCheck(false);
				}

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

				string fileDestination = Path.Combine(updateFolder, filename);

				// check digital signature
				var signer = X509Certificate.CreateFromSignedFile(fileDestination);
				/* keep these commented out lines - just in case we need all the certs from the file use this
				var coll = new X509Certificate2Collection();
				coll.Import(filePath);
				*/

				var subject = signer.Subject;
				if (!expected_subject.Contains(subject)) {
					Logger.Error("the file downloaded uses a subject that is unknown! the installation will not proceed. [subject:{0}]", subject);
					return;

				} else {
					Logger.Info("the file downloaded uses a known subject. installation and can proceed. [subject:{0}]", subject);
				}

				var hash = signer.GetCertHashString();
				if (!expected_hashes.Contains(hash)) {
					Logger.Error("the file downloaded is signed by an unknown certificate! the installation will not proceed. [hash:{0}]", hash);
					return;

				} else {
					Logger.Info("the file downloaded is signed by a known certificate. installation and can proceed. [subject:{0}]", subject);
				}

				StopZiti();

				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				Process.Start(fileDestination, "/passive");
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error has occurred");
			}
			inUpdateCheck = false;
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
			DataClient svc = (DataClient)sender;
			if (svc.CleanShutdown) {
				//then this is fine and expected - the service is shutting down
				Logger.Info("client disconnected due to clean service shutdown");
			} else {
				Logger.Error("SERVICE IS DOWN and did not exit cleanly. initiating DNS cleanup");

				MonitorStatusEvent status = new MonitorStatusEvent() { Op = "status", Status = ServiceActions.ServiceStatus() };
				EventRegistry.SendEventToConsumers(status);

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
