/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.IO;
using System.Timers;
using System.Configuration;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.IO.Compression;

using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;
using ZitiDesktopEdge.Server;
using ZitiDesktopEdge.Utility;

using NLog;
using Newtonsoft.Json;
using System.Net;
using DnsClient;
using DnsClient.Protocol;
using ZitiUpdateService.Utils;
using ZitiUpdateService.Checkers;
using System.Security.Policy;
using Newtonsoft.Json.Linq;
using System.Runtime.Remoting.Messaging;

#if !SKIPUPDATE
using ZitiUpdateService.Checkers.PeFile;
#endif

namespace ZitiUpdateService {
    public partial class UpdateService : ServiceBase {
        private const string betaStreamMarkerFile = "use-beta-stream.txt";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Settings CurrentSettings = new Settings(true);

        public bool IsBeta {
            get {
                return File.Exists(Path.Combine(exeLocation, betaStreamMarkerFile));
            }
            private set { }
        }


        private System.Timers.Timer _updateTimer = new System.Timers.Timer();
        private SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        private string exeLocation = null;

        private DataClient dataClient = new DataClient("monitor service");
        private bool running = false;
        private string asmDir = null;
        private string updateFolder = null;
        private string filePrefix = "Ziti.Desktop.Edge.Client-";
        private Version assemblyVersion = null;

        private ServiceController controller;
        private IPCServer svr = new IPCServer();
        private Task ipcServer = null;
        private Task eventServer = null;

        private const int zetHealthcheckInterval = 5;
        private SemaphoreSlim zetSemaphore = new SemaphoreSlim(1, 1);
        private System.Timers.Timer zetHealthcheck = new System.Timers.Timer();
        private int zetFailedCheckCounter = 0;

        private UpdateCheck lastUpdateCheck;
        private InstallationNotificationEvent lastInstallationNotification;

        public UpdateService() {
            InitializeComponent();

            CurrentSettings.Load();
            CurrentSettings.OnConfigurationChange += CurrentSettings_OnConfigurationChange;

            base.CanHandlePowerEvent = true;

            exeLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            Logger.Info("Initializing");
            dataClient.OnClientConnected += Svc_OnClientConnected;
            dataClient.OnTunnelStatusEvent += Svc_OnTunnelStatusEvent;
            dataClient.OnClientDisconnected += Svc_OnClientDisconnected;
            dataClient.OnShutdownEvent += Svc_OnShutdownEvent;
            dataClient.OnLogLevelEvent += ServiceClient_OnLogLevelEvent;
            dataClient.OnNotificationEvent += ServiceClient_OnNotificationEvent;

            svr.CaptureLogs = CaptureLogs;
            svr.SetLogLevel = SetLogLevel;
            svr.SetReleaseStream = SetReleaseStream;
            svr.DoUpdateCheck = DoUpdateCheck;
            svr.TriggerUpdate = TriggerUpdate;
            svr.SetAutomaticUpdateDisabled = SetAutomaticUpdateDisabled;
            svr.SetAutomaticUpdateURL = SetAutomaticUpdateURL;

            string assemblyVersionStr = Assembly.GetExecutingAssembly().GetName().Version.ToString(); //fetch from ziti?
            assemblyVersion = new Version(assemblyVersionStr);
            asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            updateFolder = Path.Combine(asmDir, "updates");
            if (!Directory.Exists(updateFolder)) {
                Directory.CreateDirectory(updateFolder);
            }
        }

        private SvcResponse SetAutomaticUpdateURL(string url) {
            SvcResponse failure = new SvcResponse();
            failure.Code = (int)ErrorCodes.URL_INVALID;
            failure.Error = $"The url supplied is invalid: \n{url}\n";
            failure.Message = "Failure";


            SvcResponse r = new SvcResponse();
            if (url == null || !url.StartsWith("http")) {
                return failure;
            } else {
                // check the url exists and appears correct...
                var check = new GithubCheck(assemblyVersion, url);

                if (check != null) {
                    var v = check.GetNextVersion();

                    if (v == null) {
                        return failure;
                    }
                    if (v.Revision.ToString().Trim() == "") {
                        return failure;
                    }
                }

                checkUpdateImmediately();

                CurrentSettings.AutomaticUpdateURL = url;
                CurrentSettings.Write();
                r.Message = "Success";
            }
            return r;
        }

        private void CurrentSettings_OnConfigurationChange(object sender, ControllerEvent e) {
            MonitorServiceStatusEvent evt;
            if (lastInstallationNotification != null) {
                evt = lastInstallationNotification;
            } else {
                evt = new MonitorServiceStatusEvent() {
                    Code = 0,
                    Error = "",
                    Message = "Configuration Changed",
                    Type = "Status",
                    Status = ServiceActions.ServiceStatus(),
                    ReleaseStream = IsBeta ? "beta" : "stable",
                    AutomaticUpgradeDisabled = CurrentSettings.AutomaticUpdatesDisabled.ToString(),
                    AutomaticUpgradeURL = CurrentSettings.AutomaticUpdateURL,
                };
            }
            Logger.Debug($"notifying consumers of change to CurrentSettings. AutomaticUpdates status = {(CurrentSettings.AutomaticUpdatesDisabled ? "disabled" : "enabled")}");
            EventRegistry.SendEventToConsumers(evt);
        }

        private SvcResponse SetAutomaticUpdateDisabled(bool disabled) {
            if (lastInstallationNotification != null) {
                lastInstallationNotification.AutomaticUpgradeDisabled = disabled.ToString();
            }
            CurrentSettings.AutomaticUpdatesDisabled = disabled;
            CurrentSettings.Write();
            SvcResponse r = new SvcResponse();
            r.Message = "Success";
            return r;
        }

        private SvcResponse TriggerUpdate() {
            SvcResponse r = new SvcResponse();
            r.Message = "Initiating Update";

            Task.Run(() => { installZDE(lastUpdateCheck); });
            return r;
        }


        private void checkUpdateImmediately() {
            try {
                CheckUpdate(null, null);
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in CheckUpdate");
            }
        }

        private StatusCheck DoUpdateCheck() {
            StatusCheck r = new StatusCheck();

            UpdateCheck check = getCheck(assemblyVersion);

            r.Code = check.Avail;
            r.ReleaseStream = IsBeta ? "beta" : "stable";
            switch (r.Code) {
                case -1:
                    r.Message = $"An update is available: {check.GetNextVersion()}";
                    r.UpdateAvailable = true;
                    Logger.Debug("Update {0} is published on {1}", check.GetNextVersion(), check.PublishDate);
                    checkUpdateImmediately();
                    break;
                case 0:
                    r.Message = $"The current version [{assemblyVersion}] is the latest";
                    break;
                case 1:
                    r.Message = $"Your version [{assemblyVersion}] is newer than the latest release";
                    break;
                default:
                    r.Message = "Update check failed";
                    break;
            }
            return r;
        }

        private void SetLogLevel(string level) {
            try {
                Logger.Info("request to change log level received: {0}", level);
                if (("" + level).ToLower().Trim() == "verbose") {
                    level = "trace";
                    Logger.Info("request to change log level to verbose - but using trace instead");
                }
                var l = LogLevel.FromString(level);
                foreach (var rule in LogManager.Configuration.LoggingRules) {
                    rule.EnableLoggingForLevel(l);
                    rule.SetLoggingLevels(l, LogLevel.Fatal);
                }

                LogManager.ReconfigExistingLoggers();
                Logger.Info("logger reconfigured to log at level: {0}", l);
            } catch (Exception e) {
                Logger.Error(e, "Could NOT set the log level for loggers??? {0}", e.Message);
            }
        }

        private void SetReleaseStream(string stream) {
            string markerFile = Path.Combine(exeLocation, betaStreamMarkerFile);
            if (stream == "beta") {
                if (IsBeta) {
                    Logger.Debug("already using beta stream. No action taken");
                } else {
                    Logger.Info("Setting update service to use beta stream!");
                    using (File.Create(markerFile)) {

                    }
                    AccessUtils.GrantAccessToFile(markerFile); //allow anyone to delete this manually if need be...
                    Logger.Debug("added marker file: {0}", markerFile);
                }
            } else {
                if (!IsBeta) {
                    Logger.Debug("already using release stream. No action taken");
                } else {
                    Logger.Info("Setting update service to use release stream!");
                    if (File.Exists(markerFile)) {
                        File.Delete(markerFile);
                        Logger.Debug("removed marker file: {0}", markerFile);
                    }
                }
            }
        }

        private string CaptureLogs() {
            try {
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

                outputIpconfigInfo(destinationLocation);
                outputSystemInfo(destinationLocation);
                outputDnsCache(destinationLocation);
                outputExternalIP(destinationLocation);
                outputTasklist(destinationLocation);
                outputRouteInfo(destinationLocation);
                outputNetstatInfo(destinationLocation);
                outputNrpt(destinationLocation);

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
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in generating system files {0}", ex.Message);
                return null;
            }
        }

        private void outputIpconfigInfo(string destinationFolder) {
            Logger.Info("capturing ipconfig information");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var ipconfigOut = Path.Combine(destinationFolder, "ipconfig.all.txt");
                Logger.Debug("copying ipconfig /all to {0}", ipconfigOut);
                startInfo.Arguments = $"/C ipconfig /all > \"{ipconfigOut}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in outputIpconfigInfo {0}", ex.Message);
            }
        }

        private void outputSystemInfo(string destinationFolder) {
            Logger.Info("capturing systeminfo");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var sysinfoOut = Path.Combine(destinationFolder, "systeminfo.txt");
                Logger.Debug("running systeminfo to {0}", sysinfoOut);
                startInfo.Arguments = $"/C systeminfo > \"{sysinfoOut}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in outputSystemInfo {0}", ex.Message);
            }
        }

        private void outputDnsCache(string destinationFolder) {
            Logger.Info("capturing dns cache information");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var dnsCache = Path.Combine(destinationFolder, "dnsCache.txt");
                Logger.Debug("running ipconfig /displaydns to {0}", dnsCache);
                startInfo.Arguments = $"/C ipconfig /displaydns > \"{dnsCache}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in outputDnsCache {0}", ex.Message);
            }
        }

        private void outputExternalIP(string destinationFolder) {
            Logger.Info("capturing external IP address using nslookup command");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var extIpFile = Path.Combine(destinationFolder, "externalIP.txt");
                Logger.Debug("running nslookup myip.opendns.com. resolver1.opendns.com to {0}", extIpFile);
                startInfo.Arguments = $"/C nslookup myip.opendns.com. resolver1.opendns.com > \"{extIpFile}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in outputExternalIP {0}", ex.Message);
            }
        }

        private void outputTasklist(string destinationFolder) {
            Logger.Info("capturing executing tasks");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var tasklistOutput = Path.Combine(destinationFolder, "tasklist.txt");
                Logger.Debug("running tasklist to {0}", tasklistOutput);
                startInfo.Arguments = $"/C tasklist > \"{tasklistOutput}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error {0}", ex.Message);
            }
        }

        private void outputRouteInfo(string destinationFolder) {
            Logger.Info("capturing network routes");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var networkRoutes = Path.Combine(destinationFolder, "network-routes.txt");
                Logger.Debug("running route print to {0}", networkRoutes);
                startInfo.Arguments = $"/C route print > \"{networkRoutes}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error {0}", ex.Message);
            }
        }

        private void outputNetstatInfo(string destinationFolder) {
            Logger.Info("capturing netstat");
            try {
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                var netstatOutput = Path.Combine(destinationFolder, "netstat.txt");
                Logger.Debug("running netstat -ano to {0}", netstatOutput);
                startInfo.Arguments = $"/C netstat -ano > \"{netstatOutput}\"";
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error {0}", ex.Message);
            }
        }

        private void outputNrpt(string destinationFolder) {
            Logger.Info("outputting NRPT rules");
            try {
                Logger.Info("outputting NRPT DnsClientNrptRule");
                string nrptRuleOutput = Path.Combine(destinationFolder, "NrptRule.txt");
                Process nrptRuleProcess = new Process();
                ProcessStartInfo nrptRuleStartInfo = new ProcessStartInfo();
                nrptRuleStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                nrptRuleStartInfo.FileName = "cmd.exe";
                nrptRuleStartInfo.Arguments = $"/C powershell \"Get-DnsClientNrptRule | sort -Property Namespace\" > \"{nrptRuleOutput}\"";
                Logger.Info("Running: {0}", nrptRuleStartInfo.Arguments);
                nrptRuleProcess.StartInfo = nrptRuleStartInfo;
                nrptRuleProcess.Start();
                nrptRuleProcess.WaitForExit();

                Logger.Info("outputting NRPT DnsClientNrptPolicy");
                string nrptOutput = Path.Combine(destinationFolder, "NrptPolicy.txt");
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/C powershell \"Get-DnsClientNrptPolicy | sort -Property Namespace\" > \"{nrptOutput}\"";
                Logger.Info("Running: {0}", startInfo.Arguments);
                process.StartInfo = startInfo;
                process.Start();
                process.WaitForExit();
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error {0}", ex.Message);
            }
        }

        public void Debug() {
            OnStart(null);// new string[] { "FilesystemCheck" });
        }

        protected override void OnStart(string[] args) {
            Logger.Debug("args: {0}", args);
            Logger.Info("ziti-monitor service is starting");

            var logs = Path.Combine(exeLocation, "logs");
            addLogsFolder(exeLocation);
            addLogsFolder(logs);
            addLogsFolder(Path.Combine(logs, "UI"));
            addLogsFolder(Path.Combine(logs, "ZitiMonitorService"));
            addLogsFolder(Path.Combine(logs, "service"));

            AccessUtils.GrantAccessToFile(Path.Combine(exeLocation, "ZitiUpdateService.exe.config")); //allow anyone to change the config file
            AccessUtils.GrantAccessToFile(Path.Combine(exeLocation, "ZitiUpdateService-log.config")); //allow anyone to change the log file config
            AccessUtils.GrantAccessToFile(Path.Combine(exeLocation, "ZitiDesktopEdge.exe.config")); //allow anyone to change the config file
            AccessUtils.GrantAccessToFile(Path.Combine(exeLocation, "ZitiDesktopEdge-log.config")); //allow anyone to change the log file config

            zetHealthcheck.Interval = zetHealthcheckInterval * 1000;
            zetHealthcheck.Elapsed += zitiEdgeTunnelAlivenessCheck;

            Logger.Info("starting ipc server");
            ipcServer = svr.startIpcServerAsync(onIpcClientAsync);
            Logger.Info("starting events server");
            eventServer = svr.startEventsServerAsync(onEventsClientAsync);

            Logger.Info("starting service watchers");
            if (!running) {
                running = true;
                Task.Run(() => {
                    SetupServiceWatchers();
                });
            }
            Logger.Info("ziti-monitor service is initialized and running");
            base.OnStart(args);
        }

        private void zitiEdgeTunnelAlivenessCheck(object sender, ElapsedEventArgs e) {
            try {
                if (zetSemaphore.Wait(TimeSpan.FromSeconds(zetHealthcheckInterval))) {
                    Logger.Trace("ziti-edge-tunnel aliveness check starts");
                    dataClient.GetStatusAsync().Wait();
                    zetSemaphore.Release();
                    Interlocked.Exchange(ref zetFailedCheckCounter, 0);
                    Logger.Trace("ziti-edge-tunnel aliveness check ends successfully");
                } else {
                    Interlocked.Add(ref zetFailedCheckCounter, 1);
                    Logger.Warn("ziti-edge-tunnel aliveness check appears blocked and has been for {} times", zetFailedCheckCounter);
                    if (zetFailedCheckCounter > 2) {
                        disableHealthCheck();
                        //after 3 failures, just terminate ziti-edge-tunnel
                        Interlocked.Exchange(ref zetFailedCheckCounter, 0); //reset the counter back to 0
                        Logger.Warn("forcefully stopping ziti-edge-tunnel as it has been blocked for too long");
                        stopProcessForcefully("ziti-edge-tunnel", "data service [ziti]");

                        Logger.Info("immediately restarting ziti-edge-tunnel");
                        ServiceActions.StartService(); //attempt to start the service
                    }
                }
            } catch (Exception ex) {
                Logger.Error("ziti-edge-tunnel aliveness check ends exceptionally: {}", ex.Message);
                Logger.Error(ex);
            }
        }

        async private Task onEventsClientAsync(StreamWriter writer) {
            try {
                Logger.Info("a new events client was connected");
                //reset to release stream
                //initial status when connecting the event stream
                MonitorServiceStatusEvent status = new MonitorServiceStatusEvent() {
                    Code = 0,
                    Error = "",
                    Message = "Success",
                    Type = "Status",
                    Status = ServiceActions.ServiceStatus(),
                    ReleaseStream = IsBeta ? "beta" : "stable",
                    AutomaticUpgradeDisabled = CurrentSettings.AutomaticUpdatesDisabled.ToString(),
                    AutomaticUpgradeURL = CurrentSettings.AutomaticUpdateURL,
                };
                await writer.WriteLineAsync(JsonConvert.SerializeObject(status));
                await writer.FlushAsync();

                //if a new client attaches - send the last update check status
                if (lastUpdateCheck != null) {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(lastInstallationNotification));
                    await writer.FlushAsync();
                }
            } catch (Exception ex) {
                Logger.Error("UNEXPECTED ERROR: {}", ex);
            }
        }

#pragma warning disable 1998 //This async method lacks 'await'
        async private Task onIpcClientAsync(StreamWriter writer) {
            Logger.Info("a new ipc client was connected");
        }
#pragma warning restore 1998 //This async method lacks 'await'

        private void addLogsFolder(string path) {
            if (!Directory.Exists(path)) {
                Logger.Info($"creating folder: {path}");
                Directory.CreateDirectory(path);
                AccessUtils.GrantAccessToDirectory(path);
            }
        }

        public void WaitForCompletion() {
            Task.WaitAll(ipcServer, eventServer);
        }

        protected override void OnStop() {
            Logger.Info("ziti-monitor OnStop was called");
            base.OnStop();
        }

        protected override void OnPause() {
            Logger.Info("ziti-monitor OnPause was called");
            base.OnPause();
        }

        protected override void OnShutdown() {
            Logger.Info("ziti-monitor OnShutdown was called");
            base.OnShutdown();
        }

        protected override void OnContinue() {
            Logger.Info("ziti-monitor OnContinue was called");
            base.OnContinue();
        }

        protected override void OnCustomCommand(int command) {
            Logger.Info("ziti-monitor OnCustomCommand was called {0}", command);
            base.OnCustomCommand(command);
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription) {
            Logger.Info("ziti-monitor OnSessionChange was called {0}", changeDescription);
            base.OnSessionChange(changeDescription);
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus) {
            Logger.Info("ziti-monitor OnPowerEvent was called {0}", powerStatus);
            if (powerStatus == PowerBroadcastStatus.Suspend) {
                // when going to sleep, make sure the healthcheck is disabled or accounts for going to sleep
                disableHealthCheck();
            }
            return base.OnPowerEvent(powerStatus);
        }

        private void SetupServiceWatchers() {
            var updateTimerInterval = ConfigurationManager.AppSettings.Get("UpdateTimer");
            var upInt = TimeSpan.Zero;
            if (!TimeSpan.TryParse(updateTimerInterval, out upInt)) {
                upInt = new TimeSpan(0, 10, 0);
            }

            if (upInt.TotalMilliseconds < 10 * 60 * 1000) {
                Logger.Warn("provided time [{0}] is too small. Using 10 minutes.", updateTimerInterval);
#if MOCKUPDATE || ALLOWFASTINTERVAL
                Logger.Info("MOCKUPDATE detected. Not limiting check to 10 minutes");
#else
				upInt = TimeSpan.Parse("0:10:0");
#endif
            }

            _updateTimer = new System.Timers.Timer();
            _updateTimer.Elapsed += CheckUpdate;
            _updateTimer.Interval = upInt.TotalMilliseconds;
            _updateTimer.Enabled = true;
            _updateTimer.Start();
            Logger.Info("Version Checker is running every {0} minutes", upInt.TotalMinutes);

            cleanOldLogs(asmDir);
            scanForStaleDownloads(updateFolder);

            checkUpdateImmediately();

            try {
                dataClient.ConnectAsync().Wait();
            } catch {
                dataClient.Reconnect();
            }

            dataClient.WaitForConnectionAsync().Wait();
        }

        private void cleanOldLogs(string whereToScan) {
            //this function will be removed in the future. it's here to clean out the old ziti-monitor*log files that
            //were there before the 1.5.0 release
            try {
                Logger.Info("Scanning for stale logs");
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

#if MOCKUPDATE
		static DateTime mockDate = DateTime.Now;
#endif
        private UpdateCheck getCheck(Version v) {
#if MOCKUPDATE
			//run with MOCKUPDATE to enable debugging/mocking the update check
			var check = new FilesystemCheck(v, -1, mockDate, "FilesysteCheck.download.mock.txt", new Version("2.1.4"));
#else

            if (string.IsNullOrEmpty(CurrentSettings.AutomaticUpdateURL)) {
                CurrentSettings.AutomaticUpdateURL = GithubAPI.ProdUrl;
                Logger.Info("Settings does not contain update url. Setting to: {}", CurrentSettings.AutomaticUpdateURL);
                CurrentSettings.Write();
            } else {
                Logger.Info("Settings contained a value for update url. Using: {}", CurrentSettings.AutomaticUpdateURL);
            }

            var check = new GithubCheck(v, CurrentSettings.AutomaticUpdateURL);
#endif
            return check;
        }

        private InstallationNotificationEvent newInstallationNotificationEvent(string version) {
            InstallationNotificationEvent info = new InstallationNotificationEvent() {
                Code = 0,
                Error = "",
                Message = "InstallationUpdate",
                Type = "Notification",
                Status = ServiceActions.ServiceStatus(),
                ReleaseStream = IsBeta ? "beta" : "stable",
                AutomaticUpgradeDisabled = CurrentSettings.AutomaticUpdatesDisabled.ToString().ToLower(),
                AutomaticUpgradeURL = CurrentSettings.AutomaticUpdateURL,
                ZDEVersion = version,
            };
            return info;
        }

        private void CheckUpdate(object sender, ElapsedEventArgs e) {
            if (e != null) {
                Logger.Debug("Timer triggered CheckUpdate at {0}", e.SignalTime);
            }
            semaphore.Wait();

            try {
                Logger.Debug("checking for update");
                var check = getCheck(assemblyVersion);

                if (check.Avail >= 0) {
                    Logger.Debug("update check complete. no update available");
                    semaphore.Release();
                    return;
                }

                Logger.Info("update is available.");
                if (!Directory.Exists(updateFolder)) {
                    Directory.CreateDirectory(updateFolder);
                }
                InstallationNotificationEvent info = newInstallationNotificationEvent(check.GetNextVersion().ToString());
                info.PublishTime = check.PublishDate;
                info.NotificationDuration = InstallationReminder();
                if (InstallationIsCritical(check.PublishDate)) {
                    info.InstallTime = DateTime.Now + TimeSpan.Parse("0:0:30");
                    Logger.Warn("Installation is critical! for ZDE version: {0}. update published at: {1}. approximate install time: {2}", info.ZDEVersion, check.PublishDate, info.InstallTime);
                    NotifyInstallationUpdates(info, true);
                    if (CurrentSettings.AutomaticUpdatesDisabled) {
                        Logger.Debug("AutomaticUpdatesDisabled is set to true. Automatic update is disabled.");
                    } else {
                        Thread.Sleep(30);
                        installZDE(check);
                    }
                } else {
                    info.InstallTime = InstallDateFromPublishDate(check.PublishDate);
                    Logger.Info("Installation reminder for ZDE version: {0}. update published at: {1}. approximate install time: {2}", info.ZDEVersion, check.PublishDate, info.InstallTime);
                    NotifyInstallationUpdates(info);
                }
                lastUpdateCheck = check;
                lastInstallationNotification = info;
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error has occurred during the check for ZDE updates");
            }
            semaphore.Release();
        }

        private void installZDE(UpdateCheck check) {
            string fileDestination = Path.Combine(updateFolder, check?.FileName);

            if (check.AlreadyDownloaded(updateFolder, check.FileName)) {
                Logger.Trace("package has already been downloaded to {0}", fileDestination);
            } else {
                Logger.Info("copying update package begins");
                check.CopyUpdatePackage(updateFolder, check.FileName);
                Logger.Info("copying update package complete");
            }

            Logger.Info("package is in {0} - moving to install phase", fileDestination);

            if (!check.HashIsValid(updateFolder, check.FileName)) {
                Logger.Warn("The file was downloaded but the hash is not valid. The file will be removed: {0}", fileDestination);
                File.Delete(fileDestination);
                return;
            }
            Logger.Debug("downloaded file hash was correct. update can continue.");
#if !SKIPUPDATE
			try {
				Logger.Info("verifying file [{}]", fileDestination);
				new SignedFileValidator(fileDestination).Verify();
				Logger.Info("SignedFileValidator complete");

				StopZiti();
				StopUI().Wait();

				Logger.Info("Running update package: " + fileDestination);
				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				Process.Start(fileDestination, "/passive");
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error during installation");
			}
#else
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
#endif
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
                if (!Directory.Exists(folder)) {
                    Logger.Debug("folder {0} does not exist. skipping", folder);
                    return;
                }
                Logger.Info("Scanning for stale downloads");
                foreach (var f in Directory.EnumerateFiles(folder)) {
                    try {
                        FileInfo fi = new FileInfo(f);
                        if (fi.Exists) {
                            if (fi.Name.StartsWith(filePrefix)) {
                                Logger.Debug("scanning for staleness: " + f);
                                string ver = Path.GetFileNameWithoutExtension(f).Substring(filePrefix.Length);
                                Version fileVersion = Version.Parse(ver);
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
                    } catch (Exception ex) {
                        Logger.Error(ex, "Unexpected exception processing {0}", f);
                    }
                }
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected exception");
            }
        }

        private void StopZiti() {
            Logger.Info("Stopping the ziti service...");
            controller = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "ziti");
            bool cleanStop = false;
            if (controller != null && controller.Status != ServiceControllerStatus.Stopped) {
                try {
                    controller.Stop();
                    Logger.Debug("Waiting for the ziti service to stop.");
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    Logger.Debug("The ziti service was stopped successfully.");
                    cleanStop = true;
                } catch (Exception e) {
                    Logger.Error(e, "Timout while trying to stop service!");
                }
            } else {
                Logger.Debug("The ziti has ALREADY been stopped successfully.");
            }
            if (!cleanStop) {
                Logger.Debug("Stopping ziti-edge-tunnel forcefully.");
                stopProcessForcefully("ziti-edge-tunnel", "data service [ziti]");
            }
        }

        private void stopProcessForcefully(string processName, string description) {
            try {
                Logger.Info("Closing the {description} process", description);
                Process[] workers = Process.GetProcessesByName(processName);
                if (workers.Length < 1) {
                    Logger.Info("No {description} process found to close.", description);
                    return;
                }
                // though strange, because we're about to kill the process, this is still
                // considered 'expected' since the monitor service is shutting it down (forcefully).
                // not clean is to indicate the process ended unexpectedly
                dataClient.ExpectedShutdown = true;

                foreach (Process worker in workers) {
                    try {
                        Logger.Info("Killing: {0}", worker);
                        if (!worker.CloseMainWindow()) {
                            //don't care right now because when called on the UI it just gets 'hidden'
                        }
                        worker.Kill();
                        worker.WaitForExit(5000);
                        Logger.Info("Stopping the {description} process killed", description);
                        worker.Dispose();
                    } catch (Exception e) {
                        Logger.Error(e, "Unexpected error when closing the {description}!", description);
                    }
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error when closing the {description}!", description);
            }
        }

        async private Task StopUI() {
            //first try to ask the UI to exit:

            MonitorServiceStatusEvent status = new MonitorServiceStatusEvent() {
                Code = 0,
                Error = "",
                Message = "Upgrading"
            };
            EventRegistry.SendEventToConsumers(status);

            await Task.Delay(1000); //wait for the event to send and give the UI time to close...

            stopProcessForcefully("ZitiDesktopEdge", "UI");
        }

        private static void Svc_OnShutdownEvent(object sender, StatusEvent e) {
            Logger.Info("the service is shutting down normally...");

            MonitorServiceStatusEvent status = new MonitorServiceStatusEvent() {
                Code = 0,
                Error = "SERVICE DOWN",
                Message = "SERVICE DOWN",
                Status = ServiceActions.ServiceStatus()
            };
            EventRegistry.SendEventToConsumers(status);
        }

        private void ServiceClient_OnLogLevelEvent(object sender, LogLevelEvent e) {
            SetLogLevel(e.LogLevel);
        }

        private void ServiceClient_OnNotificationEvent(object sender, NotificationEvent e) {
            Logger.Trace("Notification event but not acting: {0}", e.Op);
        }

        private void Svc_OnTunnelStatusEvent(object sender, TunnelStatusEvent e) {
            string dns = e?.Status?.IpInfo?.DNS;
            string version = e?.Status?.ServiceVersion.Version;
            string op = e?.Op;
            Logger.Info($"Operation {op}. running dns: {dns} at version {version}");

            SetLogLevel(e.Status.LogLevel);
        }

        private void disableHealthCheck() {
            if (zetHealthcheck.Enabled) {
                zetHealthcheck.Enabled = true;
                zetHealthcheck.Stop();
                Logger.Info("ziti-edge-tunnel health check disabled");
            } else {
                Logger.Info("ziti-edge-tunnel health check already disabled");
            }
        }

        private void enableHealthCheck() {
            if (!zetHealthcheck.Enabled) {
                zetHealthcheck.Enabled = false;
                zetHealthcheck.Start();
                Logger.Info("ziti-edge-tunnel health check enabled");
            } else {
                Logger.Info("ziti-edge-tunnel health check already enabled");
            }
        }

        private void Svc_OnClientConnected(object sender, object e) {
            Logger.Info("successfully connected to service");
            enableHealthCheck();
        }

        private void Svc_OnClientDisconnected(object sender, object e) {
            disableHealthCheck(); //no need to healthcheck when we know it's disconnected
            DataClient svc = (DataClient)sender;
            if (svc.ExpectedShutdown) {
                //then this is fine and expected - the service is shutting down
                Logger.Info("client disconnected due to clean service shutdown");
            } else {
                Logger.Error("SERVICE IS DOWN and did not exit cleanly.");

                MonitorServiceStatusEvent status = new MonitorServiceStatusEvent() {
                    Code = 10,
                    Error = "SERVICE DOWN",
                    Message = "SERVICE DOWN",
                    Type = "Status",
                    Status = ServiceActions.ServiceStatus(),
                    ReleaseStream = IsBeta ? "beta" : "stable",
                    AutomaticUpgradeDisabled = CurrentSettings.AutomaticUpdatesDisabled.ToString(),
                    AutomaticUpgradeURL = CurrentSettings.AutomaticUpdateURL,
                };
                EventRegistry.SendEventToConsumers(status);
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

        private TimeSpan InstallationReminder() {
            var installationReminderIntervalStr = ConfigurationManager.AppSettings.Get("InstallationReminder");
            var reminderInt = TimeSpan.Zero;
            if (!TimeSpan.TryParse(installationReminderIntervalStr, out reminderInt)) {
                reminderInt = new TimeSpan(0, 1, 0);
            }
            return reminderInt;
        }

        private DateTime InstallDateFromPublishDate(DateTime publishDate) {
            var installationReminderIntervalStr = ConfigurationManager.AppSettings.Get("InstallationCritical");
            var instCritTimespan = TimeSpan.Zero;
            if (!TimeSpan.TryParse(installationReminderIntervalStr, out instCritTimespan)) {
                instCritTimespan = TimeSpan.Parse("7:0:0:0");
            }
            return publishDate + instCritTimespan;
        }

        private bool InstallationIsCritical(DateTime publishDate) {
            var installationReminderIntervalStr = ConfigurationManager.AppSettings.Get("InstallationCritical");
            var instCritTimespan = TimeSpan.Zero;
            if (!TimeSpan.TryParse(installationReminderIntervalStr, out instCritTimespan)) {
                instCritTimespan = TimeSpan.Parse("7:0:0:0");
            }
            return DateTime.Now > publishDate + instCritTimespan;
        }

        private void NotifyInstallationUpdates(InstallationNotificationEvent evt) {
            NotifyInstallationUpdates(evt, false);
        }

        private void NotifyInstallationUpdates(InstallationNotificationEvent evt, bool force) {
            try {
                evt.Message = "InstallationUpdate";
                evt.Type = "Notification";
                EventRegistry.SendEventToConsumers(evt);
                Logger.Debug("NotifyInstallationUpdates: sent for version {0} is sent to the events pipe...", evt.ZDEVersion);
                return;
            } catch (Exception e) {
                Logger.Error("The notification for the installation updates for version {0} has failed: {1}", evt.ZDEVersion, e);
            }
        }
    }
}
