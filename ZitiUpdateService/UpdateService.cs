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
using System.Security.AccessControl;
using System.Security.Principal;
using System.IO.Compression;

namespace ZitiUpdateService {
	public partial class UpdateService : ServiceBase {
		private static string[] expected_hashes = new string[] { "39636E9F5E80308DE370C914CE8112876ECF4E0C" };
		private static string[] expected_subject = new string[] { @"CN=""NetFoundry, Inc."", O=""NetFoundry, Inc."", L=Herndon, S=Virginia, C=US" };

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private Timer _updateTimer = new Timer();
		private bool inUpdateCheck = false;

		private string _versionType = "latest";
		private string exeLocation = null;

		private DataClient svc = new DataClient();
		private bool running = false;
		private string asmDir = null;
		private string updateFolder = null;
		private string filePrefix = "Ziti.Desktop.Edge.Client-";
		Version assemblyVersion = null;

		ServiceController controller;
		IPCServer svr = new IPCServer();
		Task ipcServer = null;
		Task eventServer = null;
		IUpdateCheck check = null;

		public UpdateService() {
			InitializeComponent();
			exeLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			Logger.Info("Initializing");
			svc.OnClientConnected += Svc_OnClientConnected;
			svc.OnTunnelStatusEvent += Svc_OnTunnelStatusEvent;
			svc.OnClientDisconnected += Svc_OnClientDisconnected;
			svc.OnShutdownEvent += Svc_OnShutdownEvent;

			svr.CaptureLogs = GetLogs;
		}

		private string GetLogs() {
			
			string logLocation = Path.Combine(exeLocation, "logs");
			string destinationLocation = Path.Combine(exeLocation, "temp");
			string serviceLogsLocation = Path.Combine(logLocation, "service");
			string serviceLogsDest = Path.Combine(destinationLocation, "service");

			Logger.Debug("removing leftover temp folder: {0}", destinationLocation);
			try {
				Directory.Delete(destinationLocation, true);
			} catch {
				//means it doesn't exist
            }

			Directory.CreateDirectory(destinationLocation);

			Logger.Debug("copying all directories from: {0}", logLocation);
			foreach (string dirPath in Directory.GetDirectories(logLocation, "*", SearchOption.AllDirectories)) {
				Directory.CreateDirectory(dirPath.Replace(logLocation, destinationLocation));
			}

			Logger.Debug("copying all non-zip files from: {0}", logLocation);
			foreach (string newPath in Directory.GetFiles(logLocation, "*.*", SearchOption.AllDirectories)) {
				if (!newPath.EndsWith(".zip")) {
					File.Copy(newPath, newPath.Replace(logLocation, destinationLocation), true);
				}
			}

			Logger.Debug("copying service files from: {0} to {1}", serviceLogsLocation, serviceLogsDest);
			Directory.CreateDirectory(serviceLogsDest);
			foreach (string newPath in Directory.GetFiles(serviceLogsLocation, "*.*", SearchOption.TopDirectoryOnly)) {
				if (newPath.EndsWith(".log") || newPath.Contains("config.json")) { 
					Logger.Debug("copying service log: {0}", newPath);
					File.Copy(newPath, newPath.Replace(serviceLogsLocation, serviceLogsDest), true);
				}
			}

			System.Diagnostics.Process process = new System.Diagnostics.Process();
			System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
			startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			startInfo.FileName = "cmd.exe";
			var ipconfigOut = Path.Combine(destinationLocation, "ipconfig.all.txt");
			Logger.Info("copying ipconfig /all to {0}", ipconfigOut);
			startInfo.Arguments = $"/C ipconfig /all > \"{ipconfigOut}\"";
			process.StartInfo = startInfo;
			process.Start();


			Task.Delay(500).Wait();

			string zipName = Path.Combine(logLocation, DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".zip");
			ZipFile.CreateFromDirectory(destinationLocation, zipName);

			Logger.Debug("cleaning up temp folder: {0}", destinationLocation);
			try {
				Directory.Delete(destinationLocation, true);
			} catch {
				//means it doesn't exist
			}
			return zipName;
			/*
			Logger.Info("Request to collect logs received");
			var ps = System.Management.Automation.PowerShell.Create(runspace:System.Management.Automation.RunspaceMode.NewRunspace);
			string script = Path.Combine(exeLocation, "collect-logs.ps1");
			System.Diagnostics.Debug.WriteLine("=============== : " + script);
			ps.Commands.AddScript(script);
			var results = ps.Invoke();
			Logger.Info("Collected logs.");
			for(int i=0; i<results.Count; i++) {
				string r = results[i].ToString();
				Logger.Info(r);
				if (r.Contains("Log location")) {
					return r.Trim().Substring("Log location: ".Length);
				}
            }
			return "success but log location not found.";
			*/
		}

        public void Debug() {
			OnStart(null);// new string[] { "FilesystemCheck" });
		}

		protected override void OnStart(string[] args) {
			Logger.Info("ziti-monitor service is starting");
			try {
				if (ConfigurationManager.AppSettings.Get("Version") != null) _versionType = ConfigurationManager.AppSettings.Get("Version");
			} catch (Exception e) {
				Logger.Info(e.ToString());
			}

			var logs = Path.Combine(exeLocation, "logs");
			addLogsFolder(logs);
			addLogsFolder(Path.Combine(logs, "UI"));
			addLogsFolder(Path.Combine(logs, "ZitiMonitorService"));
			addLogsFolder(Path.Combine(logs, "service"));

			Logger.Info("starting ipc server");
			ipcServer = svr.startIpcServer();
			Logger.Info("starting events server");
			eventServer = svr.startEventsServer();

			Logger.Info("starting service watchers");
			if (!running) {
				running = true;
				Task.Run(() => {
					SetupServiceWatchers(args);
				});
			}
			Logger.Info("ziti-monitor service is initialized and running");
		}

		private void addLogsFolder(string path) {
			if (!Directory.Exists(path)) {
				Logger.Info($"creating folder: {path}");
				Directory.CreateDirectory(path);
			}
			Logger.Debug($"setting permissions on {path}");
			DirectorySecurity sec = Directory.GetAccessControl(path);
			// Using this instead of the "Everyone" string means we work on non-English systems.
			SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
			sec.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Modify | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
			Directory.SetAccessControl(path, sec);
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
			Logger.Info("Version Checker is running every {0} minutes", upInt.TotalMinutes);


			string assemblyVersionStr = Assembly.GetExecutingAssembly().GetName().Version.ToString(); //fetch from ziti?
			assemblyVersion = new Version(assemblyVersionStr);
			asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			updateFolder = Path.Combine(asmDir, "updates");
			cleanOldLogs(asmDir);
			scanForStaleDownloads(updateFolder);

			string updateUrl = "https://api.github.com/repos/openziti/desktop-edge-win/releases/latest"; //hardcoded on purpose
			if (args == null || args.Length < 1 || !args[0].Equals("FilesystemCheck")) {
				check = new GithubCheck(updateUrl);
			} else {
				check = new FilesystemCheck(false);
			}

			CheckUpdate(null, null); //check immediately

			try {
				svc.ConnectAsync().Wait();
			} catch {
				svc.Reconnect();
			}

			svc.WaitForConnectionAsync().Wait();
		}

		private void cleanOldLogs(string whereToScan) {
			//this function will be removed in the future. it's here to clean out the old ziti-monitor*log files that
			//were there before the 1.5.0 release
			try {
				Logger.Info("Scanning for stale downloads");
				foreach (var f in Directory.EnumerateFiles(whereToScan)) {
					FileInfo logFile = new FileInfo(f);
					if (logFile.Name.StartsWith("ziti-monitor.") && logFile.Name.EndsWith(".log")) {
						Logger.Info("removing old log file: " + logFile.Name);
						logFile.Delete();
                    }
				}
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error has occurred");
			}
		}

		private void CheckUpdate(object sender, ElapsedEventArgs e) {
			if (inUpdateCheck || check == null) return;
			inUpdateCheck = true; //simple semaphone
			try {
				Logger.Debug("checking for update");				

				if (!check.IsUpdateAvailable(assemblyVersion)) {
					Logger.Debug("update check complete. no update available");
					inUpdateCheck = false;
					return;
				}

				Logger.Info("update is available.");
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

				Logger.Info("Running update package: " + fileDestination);
				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				Process.Start(fileDestination, "/passive");
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error has occurred");
			}
			inUpdateCheck = false;
		}

		private bool isOlder(Version current) {
			int compare = current.CompareTo(assemblyVersion);
			Logger.Info("comparing current[{0}] to compare[{1}]: {2}", current.ToString(), assemblyVersion.ToString(), compare);
			if (compare < 0) {
				return true;
			} else if (compare > 0) {
				return false;
			} else {
				return false;
			}
		}

        private void scanForStaleDownloads(string folder) {
			try {
				Logger.Info("Scanning for stale downloads");
				foreach (var f in Directory.EnumerateFiles(folder)) {
					FileInfo fi = new FileInfo(f);
					if (fi.Exists) {
						if (fi.Name.StartsWith(filePrefix)) {
							Logger.Debug("scanning for staleness: " + f);
							string ver = Path.GetFileNameWithoutExtension(f).Substring(filePrefix.Length);
							Version fileVersion = new Version(ver + ".0");
							if (isOlder(fileVersion)) {
								Logger.Info("Removing old download: " + fi.Name);
								fi.Delete();
							} else {
								Logger.Debug("Retaining file. {1} is the same or newer than {1}", fi.Name, assemblyVersion);
							}
						} else {
							Logger.Debug("skipping file named {0}", f);
						}
					} else {
						Logger.Debug("file named {0} did not exist?", f);
                    }
				}
			} catch(Exception ex) {
				Logger.Error(ex, "Unexpected exception");
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
			DataClient svc = (DataClient)sender;
			if (svc.CleanShutdown) {
				//then this is fine and expected - the service is shutting down
				Logger.Info("client disconnected due to clean service shutdown");
			} else {
				Logger.Error("SERVICE IS DOWN and did not exit cleanly. initiating DNS cleanup");

				ServiceStatusEvent status = new ServiceStatusEvent() {
					Code = 10,
					Error = "SERVICE DOWN",
					Message = "SERVICE DOWN",
					Status = ServiceActions.ServiceStatus()
				};
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
