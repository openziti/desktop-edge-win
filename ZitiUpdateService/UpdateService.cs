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
using ZitiUpdateService.Utils;
using ZitiUpdateService.Checkers;

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
        private string zetSemaphoreName = "";
        private System.Timers.Timer zetHealthcheck = new System.Timers.Timer();
        private int zetFailedCheckCounter = 0;

        private UpdateCheck lastUpdateCheck;
        private InstallationNotificationEvent lastInstallationNotification;
        private volatile bool _deferredInstallPending = false;
        private volatile bool _deferToRestartPending  = false;
        private volatile bool _stagingDownloadPending = false;

        // Startup policy-polling: when HasPolicy is false at boot, poll every 5s until the
        // Group Policy registry keys appear (or until 2 minutes have elapsed).  This bounds
        // the time the UI shows unlocked/wrong values to ~5s instead of 30–60s.
        private System.Threading.Timer _startupPollTimer;
        private int _startupPollAttempts;

        public UpdateService() {
            InitializeComponent();

            try {
                CurrentSettings.Load();
            } catch {
                /* just ignore - file doesn't exist */
            }
            CurrentSettings.Write(); // allows for migration of settings
            PolicySettings.Load();      // read registry overrides; must run after Write() so policy wins
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
            svr.SetMaintenanceWindowStart = SetMaintenanceWindowStart;
            svr.SetMaintenanceWindowEnd = SetMaintenanceWindowEnd;

            string assemblyVersionStr = Assembly.GetExecutingAssembly().GetName().Version.ToString(); //fetch from ziti?
            assemblyVersion = new Version(assemblyVersionStr);
            asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            updateFolder = Path.Combine(asmDir, "updates");
            if (!Directory.Exists(updateFolder)) {
                Directory.CreateDirectory(updateFolder);
            }
        }

        private SvcResponse SetAutomaticUpdateURL(string url) {
            if (PolicySettings.IsLocked("AutomaticUpdateURL")) {
                Logger.Warn("UpdateStreamURL is managed by Group Policy, change rejected");
                return new SvcResponse {
                    Code = (int)ErrorCodes.MANAGED_BY_POLICY,
                    Error = "UpdateStreamURL is managed by Group Policy",
                    Message = "Failure",
                };
            }
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

                CurrentSettings.AutomaticUpdateURL = url;
                CurrentSettings.Write();
                r.Message = "Success";
            }
            return r;
        }

        private void CurrentSettings_OnConfigurationChange(object sender, ControllerEvent e) {
            var evt = new MonitorServiceStatusEvent() {
                Code = 0,
                Error = "",
                Message = "Configuration Changed",
                Type = "Status",
                Status = ServiceActions.ServiceStatus(),
                ReleaseStream = IsBeta ? "beta" : "stable",
            };
            ApplyEffectiveSettings(evt);
            LogStatusEvent("config change", evt);
            EventRegistry.SendEventToConsumers(evt);
        }

        private void PolicySettings_OnConfigurationChange(object sender, ControllerEvent e) {
            // If WMI fires while the startup poll is still running, cancel the poll so we don't
            // double-call checkUpdateImmediately().  If the poll timer is null we're in the
            // normal (post-startup) path and there's nothing extra to do.
            var pollTimer = Interlocked.Exchange(ref _startupPollTimer, null);
            if (pollTimer != null) {
                Logger.Info("Policy change received via WMI; cancelling startup poll timer and performing initial update check");
                pollTimer.Dispose();
                // Fall through — CurrentSettings_OnConfigurationChange below notifies the UI,
                // then we kick the first update check.
            }

            Logger.Debug("Policy settings changed, notifying consumers");
            if (PolicySettings.EffectiveAutomaticUpdatesDisabled(CurrentSettings) && (_deferredInstallPending || _deferToRestartPending || _stagingDownloadPending)) {
                Logger.Info("AutomaticUpdatesDisabled is now set; cancelling pending deferred install");
                _deferredInstallPending = false;
                _deferToRestartPending  = false;
                _stagingDownloadPending = false;
                DeferredInstallTask.Remove();
                lastInstallationNotification = null;
            }

            // If a deferred install was waiting on a maintenance-window or defer-to-restart gate
            // and the new policy state has just removed those gates, fire the install now instead
            // of waiting for the next update-timer tick.  Without this, an admin who clears the
            // gate from policy waits up to one full poll interval (10 minutes in production) for
            // the queued install to actually start.
            if (_deferredInstallPending
                && !PolicySettings.EffectiveAutomaticUpdatesDisabled(CurrentSettings)
                && IsCurrentlyInMaintenanceWindow()
                && lastUpdateCheck != null) {

                var versionToInstall = lastUpdateCheck.GetNextVersion();
                Logger.Info("Policy change unblocked deferred install; proceeding immediately with install of {0}", versionToInstall);
                _deferredInstallPending = false;
                var checkToInstall = lastUpdateCheck;
                Task.Run(() => { installZDE(checkToInstall); });
            }

            CurrentSettings_OnConfigurationChange(sender, e);

            if (pollTimer != null) {
                checkUpdateImmediately();
            }
        }

        private void ApplyEffectiveSettings(MonitorServiceStatusEvent evt) {
            evt.AutomaticUpgradeDisabled          = PolicySettings.EffectiveAutomaticUpdatesDisabled(CurrentSettings).ToString();
            evt.AutomaticUpgradeDisabledLocked    = PolicySettings.IsLocked("AutomaticUpdatesDisabled");
            evt.AutomaticUpgradeURL               = PolicySettings.EffectiveAutomaticUpdateURL(CurrentSettings) ?? GithubAPI.ProdUrl;
            evt.AutomaticUpgradeURLLocked         = PolicySettings.IsLocked("AutomaticUpdateURL");
            evt.AlivenessChecksBeforeAction       = PolicySettings.EffectiveAlivenessChecksBeforeAction(CurrentSettings);
            evt.AlivenessChecksBeforeActionLocked = PolicySettings.IsLocked("AlivenessChecksBeforeAction");
            evt.UpdateInterval                    = PolicySettings.EffectiveUpdateInterval().ToString();
            evt.UpdateIntervalLocked              = PolicySettings.IsLocked("UpdateTimer");
            evt.InstallationReminder              = PolicySettings.EffectiveInstallationReminder().ToString();
            evt.InstallationReminderLocked        = PolicySettings.IsLocked("InstallationReminder");
            evt.InstallationCritical              = PolicySettings.EffectiveInstallationCritical().ToString();
            evt.InstallationCriticalLocked        = PolicySettings.IsLocked("InstallationCritical");
            evt.MaintenanceWindowStart            = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
            evt.MaintenanceWindowStartLocked      = PolicySettings.IsLocked("MaintenanceWindowStart");
            evt.MaintenanceWindowEnd              = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
            evt.MaintenanceWindowEndLocked        = PolicySettings.IsLocked("MaintenanceWindowEnd");
            evt.DeferInstallToRestartLocked       = PolicySettings.IsLocked("DeferInstallToRestart");
            evt.DeferredInstallPending            = _deferredInstallPending;
            evt.DeferToRestartPending             = _deferToRestartPending;
            evt.StagingDownloadPending            = _stagingDownloadPending;
        }

        private void LogStatusEvent(string context, MonitorServiceStatusEvent evt) {
            Logger.Debug("{0}: sending MonitorServiceStatusEvent\n" +
                        "  AutomaticUpgradeDisabled    = {1}  (locked={2})\n" +
                        "  AutomaticUpgradeURL         = {3}  (locked={4})\n" +
                        "  AlivenessChecksBeforeAction = {5}  (locked={6})\n" +
                        "  UpdateInterval              = {7}  (locked={8})\n" +
                        "  InstallationReminder        = {9}  (locked={10})\n" +
                        "  InstallationCritical        = {11}  (locked={12})\n" +
                        "  ReleaseStream               = {13}\n" +
                        "  Status                      = {14}\n" +
                        "  Message                     = {15}",
                context,
                evt.AutomaticUpgradeDisabled    ?? "(null)", evt.AutomaticUpgradeDisabledLocked,
                evt.AutomaticUpgradeURL         ?? "(null)", evt.AutomaticUpgradeURLLocked,
                evt.AlivenessChecksBeforeAction?.ToString() ?? "(null)", evt.AlivenessChecksBeforeActionLocked,
                evt.UpdateInterval              ?? "(null)", evt.UpdateIntervalLocked,
                evt.InstallationReminder        ?? "(null)", evt.InstallationReminderLocked,
                evt.InstallationCritical        ?? "(null)", evt.InstallationCriticalLocked,
                evt.ReleaseStream               ?? "(null)",
                evt.Status                      ?? "(null)",
                evt.Message                     ?? "(null)");
        }

        private SvcResponse SetMaintenanceWindowStart(int? hour) {
            if (PolicySettings.IsLocked("MaintenanceWindowStart")) {
                return new SvcResponse { Code = (int)ErrorCodes.MANAGED_BY_POLICY, Error = "MaintenanceWindowStart is managed by policy", Message = "Failure" };
            }
            if (hour.HasValue && (hour.Value < 0 || hour.Value > 23)) {
                return new SvcResponse { Code = (int)ErrorCodes.INVALID_VALUE, Error = $"MaintenanceWindowStart must be 0-23, got {hour.Value}", Message = "Failure" };
            }
            CurrentSettings.MaintenanceWindowStart = hour;
            CurrentSettings.Write();
            return new SvcResponse { Message = "Success" };
        }

        private SvcResponse SetMaintenanceWindowEnd(int? hour) {
            if (PolicySettings.IsLocked("MaintenanceWindowEnd")) {
                return new SvcResponse { Code = (int)ErrorCodes.MANAGED_BY_POLICY, Error = "MaintenanceWindowEnd is managed by policy", Message = "Failure" };
            }
            if (hour.HasValue && (hour.Value < 0 || hour.Value > 23)) {
                return new SvcResponse { Code = (int)ErrorCodes.INVALID_VALUE, Error = $"MaintenanceWindowEnd must be 0-23, got {hour.Value}", Message = "Failure" };
            }
            CurrentSettings.MaintenanceWindowEnd = hour;
            CurrentSettings.Write();
            return new SvcResponse { Message = "Success" };
        }

        private SvcResponse SetAutomaticUpdateDisabled(bool disabled) {
            if (PolicySettings.IsLocked("AutomaticUpdatesDisabled")) {
                Logger.Warn("DisableAutomaticUpdates is managed by Group Policy, change rejected");
                return new SvcResponse {
                    Code = (int)ErrorCodes.MANAGED_BY_POLICY,
                    Error = "DisableAutomaticUpdates is managed by Group Policy",
                    Message = "Failure",
                };
            }
            if (lastInstallationNotification != null) {
                lastInstallationNotification.AutomaticUpgradeDisabled = disabled.ToString();
            }
            CurrentSettings.AutomaticUpdatesDisabled = disabled;
            CurrentSettings.Write();
            SvcResponse r = new SvcResponse();
            r.Message = "Success";
            return r;
        }

        private SvcResponse TriggerUpdate(bool forceDefer = false) {
            SvcResponse r = new SvcResponse();

            int? start = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
            int? end   = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
            bool anyTime = !start.HasValue || !end.HasValue || start.Value == end.Value;
            bool inWindow = anyTime || IsCurrentlyInMaintenanceWindow();

            Logger.Info("TriggerUpdate requested. MaintenanceWindow={0}-{1}, anyTime={2}, inWindow={3}, now={4}",
                start.HasValue ? start.Value.ToString() : "null",
                end.HasValue   ? end.Value.ToString()   : "null",
                anyTime, inWindow, DateTime.Now.ToString("HH:mm") + " (local)");

            if (!inWindow) {
                DateTime next = SnapToMaintenanceWindow(DateTime.Now);
                Logger.Info("TriggerUpdate deferred: outside maintenance window {0}-{1}. Will install when window opens at {2}",
                    start, end, next.ToString("g") + " (local)");
                _deferredInstallPending = true;
                r.Message = "Update scheduled for maintenance window";
                r.Code = (int)ErrorCodes.NO_ERROR;
                return r;
            }

            Logger.Info("TriggerUpdate proceeding, inside maintenance window");
            _deferredInstallPending = false;

            if (forceDefer || PolicySettings.EffectiveDeferInstallToRestart(CurrentSettings)) {
                Logger.Info("TriggerUpdate: {0}, staging installer for next restart",
                    forceDefer ? "user requested deferred install" : "DeferInstallToRestart=True");
                _stagingDownloadPending = true;
                r.Message = "Downloading update for next restart...";
                r.Code = (int)ErrorCodes.NO_ERROR;
                Task.Run(() => { StageInstallForRestart(lastUpdateCheck); });
                return r;
            }

            r.Message = "Initiating Update";
            Task.Run(() => { installZDE(lastUpdateCheck); });
            return r;
        }

        private void StageInstallForRestart(UpdateCheck check) {
            try {
                if (check == null) {
                    Logger.Warn("StageInstallForRestart: no update check available, cannot stage");
                    return;
                }
                string fileDestination = Path.Combine(updateFolder, check.FileName);
                if (check.AlreadyDownloaded(updateFolder, check.FileName)) {
                    Logger.Debug("StageInstallForRestart: installer already downloaded at {0}", fileDestination);
                } else {
                    Logger.Info("StageInstallForRestart: downloading installer");
                    check.CopyUpdatePackage(updateFolder, check.FileName);
                    Logger.Info("StageInstallForRestart: download complete");
                }
                if (!check.HashIsValid(updateFolder, check.FileName)) {
                    Logger.Warn("StageInstallForRestart: hash invalid, removing {0}", fileDestination);
                    File.Delete(fileDestination);
                    _stagingDownloadPending = false;
                    _deferToRestartPending  = false;
                    if (lastInstallationNotification != null) NotifyInstallationUpdates(lastInstallationNotification, true);
                    return;
                }
#if !SKIPUPDATE
                new SignedFileValidator(fileDestination).Verify();
                DeferredInstallTask.Register(fileDestination);
                Logger.Info("StageInstallForRestart: installer staged at {0}; will run on next system restart", fileDestination);
#else
                Logger.Warn("SKIPUPDATE IS SET - NOT staging installer for restart");
#endif
                // Transition: downloading → staged
                _stagingDownloadPending = false;
                _deferToRestartPending  = true;
                // Push updated state to connected UI clients
                if (lastInstallationNotification != null) {
                    NotifyInstallationUpdates(lastInstallationNotification, true);
                }
            } catch (Exception ex) {
                Logger.Error(ex, "StageInstallForRestart: unexpected error staging installer");
                _stagingDownloadPending = false;
                _deferToRestartPending  = false;
                if (lastInstallationNotification != null) NotifyInstallationUpdates(lastInstallationNotification, true);
            }
        }


        private void checkUpdateImmediately() {
            try {
                CheckUpdate(null, null);
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error in CheckUpdate");
            }
        }

        /// <summary>
        /// Periodic callback during startup: reloads policy every 5 s until HasPolicy is true
        /// or 2 minutes have elapsed.  When policy is found the UI is notified immediately and
        /// the first update check is performed.  If WMI fires first, it cancels this timer via
        /// <see cref="PolicySettings_OnConfigurationChange"/> so we don't double-check.
        /// </summary>
        private void OnStartupPolicyPoll(object state) {
            _startupPollAttempts++;
            PolicySettings.Load();

            bool policyFound = PolicySettings.HasPolicy;
            bool timedOut    = _startupPollAttempts >= 24; // 24 × 5 s = 120 s

            if (!policyFound && !timedOut) {
                Logger.Trace("Startup policy poll attempt {0}: policy not yet in registry", _startupPollAttempts);
                return; // keep polling
            }

            // Stop the timer (swap to null so PolicySettings_OnConfigurationChange won't double-fire).
            var t = Interlocked.Exchange(ref _startupPollTimer, null);
            t?.Dispose();

            if (policyFound) {
                Logger.Info("Startup policy poll: policy found after {0} attempt(s); notifying UI and checking for updates", _startupPollAttempts);
                // Start the WMI watcher now that the registry key exists, so the
                // subsequent RetryWatchIfNeeded inside CheckUpdate is a no-op and
                // does not trigger a redundant second Load().
                PolicySettings.StartWatching();
                PolicySettings_OnConfigurationChange(null, null); // push locked state to UI
            } else {
                Logger.Info("Startup policy poll: no policy after 2 minutes; proceeding without policy");
            }
            checkUpdateImmediately();
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
            if (PolicySettings.IsLocked("AutomaticUpdateURL")) {
                Logger.Warn("UpdateStreamURL is managed by Group Policy, SetReleaseStream rejected");
                return;
            }
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

            PolicySettings.OnConfigurationChange += PolicySettings_OnConfigurationChange;
            PolicySettings.StartWatching();

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
            bool succeeded = false;
            try {
                if (zetSemaphore.Wait(TimeSpan.FromSeconds(zetHealthcheckInterval))) {
                    zetSemaphoreName = $"acquired at: {DateTime.UtcNow}";
                    Logger.Trace("ziti-edge-tunnel aliveness check starts, zetSemaphore lock acquired");
                    dataClient.GetStatusAsync().Wait();
                    Logger.Trace("ziti-edge-tunnel aliveness check {} ends successfully", zetSemaphoreName);
                    succeeded = true;
                } else {
                    Logger.Trace("ziti-edge-tunnel aliveness check {} semaphore could not be aquired", zetSemaphoreName);
                }
            }
            catch (Exception ex) {
                Logger.Error("ziti-edge-tunnel aliveness check {} ended exceptionally. released semaphore but not resetting counter", zetSemaphoreName);
                zetSemaphore.Release();
                Logger.Error(ex);
            }

            if (succeeded) {
                Interlocked.Exchange(ref zetFailedCheckCounter, 0);
                zetSemaphore.Release();
                Logger.Trace("status call succeeded, reset to 0 and zetSemaphore {} released", zetSemaphoreName);
                zetSemaphoreName = "unset";
            } else {
                Interlocked.Add(ref zetFailedCheckCounter, 1);
                int alivenessThreshold = PolicySettings.EffectiveAlivenessChecksBeforeAction(CurrentSettings);
                Logger.Warn("ziti-edge-tunnel aliveness check {} appears blocked and has been for {0} times. AlivenessChecksBeforeAction:{1}", zetSemaphoreName, zetFailedCheckCounter, alivenessThreshold);
                if (alivenessThreshold > 0) {
                    if (zetFailedCheckCounter > alivenessThreshold) {
                        disableHealthCheck();
                        //after 'n' failures, just terminate ziti-edge-tunnel
                        Interlocked.Exchange(ref zetFailedCheckCounter, 0); //reset the counter back to 0
                        Logger.Debug("status call failed, reset to 0 and zetSemaphore {} released", zetSemaphoreName);
                        zetSemaphoreName = "unset";
                        zetSemaphore.Release();

                        Logger.Warn("forcefully stopping ziti-edge-tunnel as it has been blocked for too long");
                        stopProcessForcefully("ziti-edge-tunnel", "data service [ziti]");

                        Logger.Info("immediately restarting ziti-edge-tunnel");
                        ServiceActions.StartService(); //attempt to start the service
                    }
                }
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
                };
                ApplyEffectiveSettings(status);
                LogStatusEvent("new client connected", status);
                await writer.WriteLineAsync(JsonConvert.SerializeObject(status));
                await writer.FlushAsync();

                //if a new client attaches - send the last update check status
                if (lastUpdateCheck != null && lastInstallationNotification != null) {
                    lastInstallationNotification.DeferredInstallPending = _deferredInstallPending;
                    lastInstallationNotification.DeferToRestartPending  = _deferToRestartPending;
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
            PolicySettings.StopWatching();
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
            var upInt = PolicySettings.EffectiveUpdateInterval();

            if (upInt.TotalMilliseconds < 10 * 60 * 1000) {
#if MOCKUPDATE || ALLOWFASTINTERVAL
                Logger.Debug("Fast interval enabled: using {0} (minimum floor bypassed)", upInt);
#else
                Logger.Warn("provided time [{0}] is too small. Using 10 minutes.", upInt);
                upInt = TimeSpan.Parse("0:10:0");
#endif
            }

            _updateTimer = new System.Timers.Timer();
            _updateTimer.Elapsed += CheckUpdate;
            _updateTimer.Interval = upInt.TotalMilliseconds;
            _updateTimer.Enabled = true;
            _updateTimer.Start();

            if (upInt.TotalSeconds > 120) {
                Logger.Info("Version Checker is running every {0} minutes", upInt.TotalMinutes);
            } else {
                Logger.Info("Version Checker is running every {0} seconds", upInt.TotalSeconds);
            }

            cleanOldLogs(asmDir);
            scanForStaleDownloads(updateFolder);
            if (DeferredInstallTask.IsRegistered()) {
                Logger.Info("Deferred install task is registered at startup; removing it (installer already ran)");
                DeferredInstallTask.Remove();
            } else {
                Logger.Info("No deferred install task registered at startup");
            }

            if (PolicySettings.HasPolicy) {
                checkUpdateImmediately();
            } else {
                // No policy registry key at startup — Group Policy may not have applied yet.
                // Poll every 5s for up to 2 minutes so the UI updates within ~5s of GP applying
                // rather than waiting for the WMI watcher (which can take 30–60s to fire).
                Logger.Info("No policy found at startup; polling every 5s for Group Policy to apply (max 2 min)");
                _startupPollAttempts = 0;
                _startupPollTimer = new System.Threading.Timer(OnStartupPolicyPoll, null, 5000, 5000);
            }

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

            string updateUrl = PolicySettings.EffectiveAutomaticUpdateURL(CurrentSettings);
            if (string.IsNullOrEmpty(updateUrl)) {
                updateUrl = GithubAPI.ProdUrl;
                if (!PolicySettings.IsLocked("AutomaticUpdateURL")) {
                    CurrentSettings.AutomaticUpdateURL = updateUrl;
                    CurrentSettings.Write();
                }
                Logger.Info("No update URL configured. Using default: {}", updateUrl);
            }
            Logger.Debug("update stream URL: {}", updateUrl);

            var check = new GithubCheck(v, updateUrl);
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
                ZDEVersion = version,
            };
            ApplyEffectiveSettings(info);
            return info;
        }

        private void CheckUpdate(object sender, ElapsedEventArgs e) {
            PolicySettings.RetryWatchIfNeeded();

            if (e != null) {
                Logger.Debug("Timer triggered CheckUpdate at {0}", e.SignalTime);
            }
            // Drop concurrent invocations rather than queueing them.  Multiple call sites
            // (timer tick, IPC DoUpdateCheck, policy-change handler, startup poll) can fire
            // CheckUpdate within the same instant; without this, each invocation waits in turn
            // and re-runs the entire check, producing N×log spam and N×network requests for
            // the same state. The first caller wins; the rest log a debug line and return.
            if (!semaphore.Wait(0)) {
                Logger.Debug("CheckUpdate already in progress; skipping concurrent invocation");
                return;
            }

            try {
                if (_deferredInstallPending) {
                    bool inWindow = IsCurrentlyInMaintenanceWindow();
                    if (lastUpdateCheck != null && inWindow) {
                        Logger.Info("Deferred install: maintenance window is now open, proceeding with install of {0}", lastUpdateCheck.GetNextVersion());
                        _deferredInstallPending = false;
                        semaphore.Release();
                        Task.Run(() => { installZDE(lastUpdateCheck); });
                        return;
                    }
                    int? wStart = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
                    int? wEnd   = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
                    Logger.Info("Deferred install pending, update {0} queued. Window={1}-{2}, inWindow={3}, now={4}",
                        lastUpdateCheck != null ? lastUpdateCheck.GetNextVersion().ToString() : "(verifying...)",
                        wStart.HasValue ? wStart.Value.ToString() : "any",
                        wEnd.HasValue   ? wEnd.Value.ToString()   : "any",
                        inWindow,
                        DateTime.Now.ToString("HH:mm") + " (local)");
                    if (lastUpdateCheck != null) {
                        // Version already known; skip network call and just wait for window
                        semaphore.Release();
                        return;
                    }
                    // lastUpdateCheck is null; fall through to populate it via network check
                }

                Logger.Debug("checking for update");
                var check = getCheck(assemblyVersion);

                if (check.Avail >= 0) {
                    Logger.Debug("update check complete. no update available");
                    semaphore.Release();
                    return;
                }

                Logger.Info("update is available.");

                if (_deferredInstallPending) {
                    // First check since service start; record the version and wait for window
                    lastUpdateCheck = check;
                    semaphore.Release();
                    return;
                }

                if (!Directory.Exists(updateFolder)) {
                    Directory.CreateDirectory(updateFolder);
                }
                InstallationNotificationEvent info = newInstallationNotificationEvent(check.GetNextVersion().ToString());
                info.PublishTime = check.PublishDate;
                info.NotificationDuration = InstallationReminder();
                bool updatesDisabled = PolicySettings.EffectiveAutomaticUpdatesDisabled(CurrentSettings);
                Logger.Debug("InstallationIsCritical check: publishDate={0:u} (UTC), threshold={1}, criticalAfter={2} (local), now={3} (local)",
                    check.PublishDate.ToUniversalTime(), PolicySettings.EffectiveInstallationCritical(),
                    check.PublishDate.ToLocalTime() + PolicySettings.EffectiveInstallationCritical(), DateTime.Now);
                if (InstallationIsCritical(check.PublishDate)) {
                    if (updatesDisabled) {
                        Logger.Info("Update {0} is critical but AutomaticUpdatesDisabled is set; skipping notification and install", info.ZDEVersion);
                    } else if (IsCurrentlyInMaintenanceWindow()) {
                        info.InstallTime = DateTime.Now + TimeSpan.Parse("0:0:30");
                        Logger.Warn("Installation is critical! for ZDE version: {0}. update published at: {1:u} (UTC). approximate install time: {2} (local)", info.ZDEVersion, check.PublishDate.ToUniversalTime(), info.InstallTime);
                        NotifyInstallationUpdates(info, true);
                        Thread.Sleep(30000);
                        installZDE(check);
                    } else {
                        info.InstallTime = SnapToMaintenanceWindow(DateTime.Now);
                        Logger.Warn("Installation is critical for ZDE version: {0} but outside maintenance window, deferring to {1} (local)", info.ZDEVersion, info.InstallTime);
                        NotifyInstallationUpdates(info, true);
                    }
                } else {
                    info.InstallTime = InstallDateFromPublishDate(check.PublishDate);
                    if (updatesDisabled) {
                        Logger.Info("Update {0} available but AutomaticUpdatesDisabled is set; skipping notification", info.ZDEVersion);
                    } else {
                        Logger.Info("Installation reminder for ZDE version: {0}. update published at: {1:u} (UTC). approximate install time: {2} (local)", info.ZDEVersion, check.PublishDate.ToUniversalTime(), info.InstallTime);
                        NotifyInstallationUpdates(info);
                    }
                }
                lastUpdateCheck = check;
                lastInstallationNotification = updatesDisabled ? null : info;
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error has occurred during the check for ZDE updates");
            }
            semaphore.Release();
        }

        private void SendUpgradeProgress(string phase) {
            MonitorServiceStatusEvent progressEvent = new MonitorServiceStatusEvent() {
                Code = 0,
                Error = "",
                Message = "UpdateProgress:" + phase,
                Status = ServiceActions.ServiceStatus(),
            };
            EventRegistry.SendEventToConsumers(progressEvent);
        }

        private void SendUpgradeFailure(string reason) {
            MonitorServiceStatusEvent failureEvent = new MonitorServiceStatusEvent() {
                Code = 1,
                Error = reason,
                Message = "UpdateFailed:" + reason,
                Status = ServiceActions.ServiceStatus(),
            };
            EventRegistry.SendEventToConsumers(failureEvent);
        }

        private void installZDE(UpdateCheck check) {
            int? start = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
            int? end   = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
            bool anyTime = !start.HasValue || !end.HasValue || start.Value == end.Value;
            string windowStr = anyTime ? "any" : $"{start.Value}-{end.Value}";
            Logger.Info("installZDE called at {0} (local). MaintenanceWindow={1}. InWindow={2}. Version={3}",
                DateTime.Now.ToString("HH:mm"),
                windowStr,
                IsCurrentlyInMaintenanceWindow(),
                check?.GetNextVersion());
            string fileDestination = Path.Combine(updateFolder, check?.FileName);

            if (check.AlreadyDownloaded(updateFolder, check.FileName)) {
                Logger.Trace("package has already been downloaded to {0}", fileDestination);
            } else {
                SendUpgradeProgress("Downloading");
                Logger.Info("copying update package begins");
                try {
                    check.CopyUpdatePackage(updateFolder, check.FileName);
                } catch (Exception e) {
                    Logger.Error("copying update package failed! {0}", e);
                    SendUpgradeFailure("Download failed");
                    return;
                }
                Logger.Info("copying update package complete");
            }

            Logger.Info("package is in {0} - moving to install phase", fileDestination);
            SendUpgradeProgress("Verifying");

            if (!check.HashIsValid(updateFolder, check.FileName)) {
                Logger.Warn("The file was downloaded but the hash is not valid. The file will be removed: {0}", fileDestination);
                File.Delete(fileDestination);
                SendUpgradeFailure("Hash verification failed");
                return;
            }
            Logger.Debug("downloaded file hash was correct. update can continue.");
#if !SKIPUPDATE
			try {
				Logger.Info("verifying file [{}]", fileDestination);
				new SignedFileValidator(fileDestination).Verify();
				Logger.Info("SignedFileValidator complete");

				SendUpgradeProgress("Installing");
				StopZiti();
				StopUI().Wait();

				Logger.Info("Running update package: " + fileDestination);
				// shell out to a new process and run the uninstall, reinstall steps which SHOULD stop this current process as well
				Process.Start(fileDestination, "/passive");
			} catch (Exception ex) {
				Logger.Error(ex, "Unexpected error during installation");
				SendUpgradeFailure("Installation failed");
			}
#else
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
            Logger.Warn("SKIPUPDATE IS SET - NOT PERFORMING UPDATE of version: {} published at {}", check.GetNextVersion(), check.PublishDate);
#endif
        }

        private bool isOlder(Version current) {
            int compare = current.CompareTo(assemblyVersion);
            Logger.Info("stale download check: file={0}, running={1}, isOlder={2}", current, assemblyVersion, compare < 0);
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
                    Logger.Error(e, "Timeout while trying to stop service!");
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
                string logLocation = Path.Combine(exeLocation, "logs");

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
                        MiniDump.CreateMemoryDump(worker, Path.Combine(logLocation, "ziti-edge-tunnel.stalled.dmp"));
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
            /* monitoring thread needs more testing
            Thread monitoringThread = new Thread(() =>
            {
                var monitor = new MinidumpMonitor("ziti-edge-tunnel");
                monitor.StartMonitoring();
            });

            monitoringThread.IsBackground = true;
            monitoringThread.Start();

            Logger.Info("Monitoring started in a separate thread. Press Enter to exit.");
            */
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
                };
                ApplyEffectiveSettings(status);
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
            return PolicySettings.EffectiveInstallationReminder();
        }

        private DateTime InstallDateFromPublishDate(DateTime publishDate) {
            // check.PublishDate is UTC (comes from the release-stream JSON). Convert to local
            // before adding the critical-install TimeSpan so SnapToMaintenanceWindow compares
            // against local-time window bounds (its .Hour check assumes local time), and so the
            // resulting InstallTime is consistent with the other InstallTime assignments that
            // use DateTime.Now (local).
            DateTime raw = publishDate.ToLocalTime() + PolicySettings.EffectiveInstallationCritical();
            return SnapToMaintenanceWindow(raw);
        }

        private bool InstallationIsCritical(DateTime publishDate) {
            // publishDate is UTC (from the release-stream JSON). Convert to local so the
            // comparison against DateTime.Now (local) is timezone-consistent. Without this
            // conversion, users in negative UTC offsets would see the critical threshold
            // appear to be in the future and never auto-install.
            return DateTime.Now > publishDate.ToLocalTime() + PolicySettings.EffectiveInstallationCritical();
        }

        /// <summary>
        /// Returns true when the current wall-clock time falls inside the effective
        /// maintenance window, or when no window is configured (any time is allowed).
        /// </summary>
        private bool IsCurrentlyInMaintenanceWindow() {
            int? start = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
            int? end   = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
            if (!start.HasValue || !end.HasValue) return true;
            if (start.Value == end.Value) return true;  // 0/0 or equal values = any time
            return IsInWindow(DateTime.Now.Hour, start.Value, end.Value);
        }

        /// <summary>
        /// Snaps <paramref name="dt"/> forward to the next opening of the maintenance window.
        /// Returns <paramref name="dt"/> unchanged when no window is configured or the time
        /// already falls within the window.
        /// </summary>
        private DateTime SnapToMaintenanceWindow(DateTime dt) {
            int? start = PolicySettings.EffectiveMaintenanceWindowStart(CurrentSettings);
            int? end   = PolicySettings.EffectiveMaintenanceWindowEnd(CurrentSettings);
            if (!start.HasValue || !end.HasValue) return dt;
            if (start.Value == end.Value) return dt;  // any time

            if (IsInWindow(dt.Hour, start.Value, end.Value)) return dt;

            // Advance to the next occurrence of the window start hour
            DateTime candidate = dt.Date.AddHours(start.Value);
            if (candidate <= dt) candidate = candidate.AddDays(1);
            return candidate;
        }

        private bool IsInWindow(int hour, int windowStart, int windowEnd) {
            if (windowStart < windowEnd) {
                return hour >= windowStart && hour < windowEnd;
            }
            // Crosses midnight (e.g. 22:00–04:00)
            return hour >= windowStart || hour < windowEnd;
        }

        private void NotifyInstallationUpdates(InstallationNotificationEvent evt) {
            NotifyInstallationUpdates(evt, false);
        }

        private void NotifyInstallationUpdates(InstallationNotificationEvent evt, bool force) {
            try {
                evt.Message = "InstallationUpdate";
                evt.Type = "Notification";
                // Always stamp the current pending-install flags so the UI stays consistent
                // even when a timer tick fires a new notification after staging has occurred.
                evt.DeferredInstallPending = _deferredInstallPending;
                evt.DeferToRestartPending  = _deferToRestartPending;
                evt.StagingDownloadPending = _stagingDownloadPending;
                EventRegistry.SendEventToConsumers(evt);
                Logger.Debug("NotifyInstallationUpdates: sent for version {0} is sent to the events pipe...", evt.ZDEVersion);
                return;
            } catch (Exception e) {
                Logger.Error("The notification for the installation updates for version {0} has failed: {1}", evt.ZDEVersion, e);
            }
        }
    }
}
