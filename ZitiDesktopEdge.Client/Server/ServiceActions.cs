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
using System.Management;
using System.ServiceProcess;
using NLog;

namespace ZitiDesktopEdge.Server {
    public static class ServiceActions {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static int serviceWaitTime = 60; //one minute

        private static ServiceController sc = new ServiceController("ziti");
        public static string ServiceStatus() {
            try {
                var status = sc.Status;
                Logger.Debug("service status asked for. current value: {0}", sc.Status);

                if (sc.Status == ServiceControllerStatus.StopPending) {
                    //ServiceControllerStatus is reporting 'stop pending' when the service crashes or is terminated by a user
                    //this is INCORRECT as the service is dead - it is not pending. Test for the process by name and if there's
                    //still a process - cool. if NOT - send the 'stopped' message...
                    var procs = System.Diagnostics.Process.GetProcessesByName("ziti-edge-tunnel");
                    if (procs != null && procs.Length == 1) {
                        // if there's more than one ziti-edge-tunnel that'd be bad too but we can't account for that here
                        Logger.Warn("ServiceControllerStatus is StopPending but there is NO ziti-edge-tunnel process! report service is stopped");
                        return ServiceControllerStatus.Stopped.ToString();
                    }
                }
                return status.ToString();
            } catch(Exception e) {
                Logger.Warn(e.Message);
            }
            return null;
        }

        public static string StartService() {
            Logger.Info($"request to start ziti service received... waiting up to {serviceWaitTime}s for service start...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, new System.TimeSpan(0, 0, serviceWaitTime));
            Logger.Info("request to start ziti service received... complete...");
            return ServiceStatus();
        }

        public static string StopService() {
            try {
                Logger.Info($"request to stop ziti service received... waiting up to {serviceWaitTime}s for service stop...");
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, new System.TimeSpan(0, 0, serviceWaitTime));
                Logger.Info("request to stop ziti service received... complete...");
                return ServiceStatus();
            } catch (Exception e) {
                Logger.Error("failed to stop service using ServiceController. Attempting to find and kill the ziti process directly");

                var zetProcesses = Process.GetProcesses().Where(p => p.ProcessName == "ziti-edge-tunnel");

                foreach (var process in zetProcesses) {
                    try {
                        Logger.Warn($"attempting to forcefully terminate process: {process.Id}");
                        process.Kill();
                        if (process.WaitForExit(30 * 1000)) { // wait for 30s
                            Logger.Warn($"terminated process forcefully: {process.Id}");
                        } else {
                            Logger.Error($"waited 30s, could not terminate process: {process.Id}");
                        }
                    } catch (Exception ex) {
                        Logger.Error($"failed to forcefully terminate process: {process.Id}!!! Error Msg: {ex.Message}");
                    }
                }

                Logger.Info("graceful shutdown failed. Removing all NRPT rules");
                RemoveNrptRules();
                Logger.Info("graceful shutdown failed. Removing all ziti-tun* interfaces");
                RemoveZitiTunInterfaces();

                throw e;
            }
        }

        static void RemoveNrptRules() {
            Process nrptRuleProcess = new Process();
            ProcessStartInfo nrptRuleStartInfo = new ProcessStartInfo();
            nrptRuleStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            nrptRuleStartInfo.FileName = "cmd.exe";
            var cmd = "Get-DnsClientNrptRule | Where { $_.Comment.StartsWith('Added by ziti-edge-tunnel') } | Remove-DnsClientNrptRule -ErrorAction SilentlyContinue -Force";
            nrptRuleStartInfo.Arguments = $"/C powershell \"{cmd}\"";
            Logger.Info("Running: {0}", nrptRuleStartInfo.Arguments);
            nrptRuleProcess.StartInfo = nrptRuleStartInfo;
            nrptRuleProcess.Start();
            if (nrptRuleProcess.WaitForExit(60 * 1000)) { // wait for 60s
                Logger.Debug("NRPT rules have been removed");
            } else {
                Logger.Error($"waited 60s, could not remove NRPT rules?!");
            }
        }

        static void RemoveZitiTunInterfaces() {
            string query = "SELECT * FROM Win32_NetworkAdapter WHERE Name LIKE 'ziti%'";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
            ManagementObjectCollection results = searcher.Get();

            foreach (ManagementObject obj in results) {
                try {
                    obj.InvokeMethod("Disable", null);
                    Console.WriteLine("Disabled interface: " + obj["Name"]);
                } catch (Exception e) {
                    Console.WriteLine("Error disabling interface: " + obj["Name"] + " - " + e.Message);
                }
            }
        }
    }
}
