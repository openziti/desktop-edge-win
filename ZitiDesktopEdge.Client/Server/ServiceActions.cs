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

﻿using System.ServiceProcess;

using NLog;

namespace ZitiDesktopEdge.Server {
    public static class ServiceActions {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private static ServiceController sc = new ServiceController("ziti");
        public static string ServiceStatus() {
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
        }

        public static string StartService() {
            Logger.Info("request to start ziti service received... processing...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, new System.TimeSpan(0,0,30));
            Logger.Info("request to start ziti service received... complete...");
            return ServiceStatus();
        }

        public static string StopService() {
            Logger.Info("request to stop ziti service received... processing...");
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, new System.TimeSpan(0, 0, 30));
            Logger.Info("request to stop ziti service received... complete...");
            return ServiceStatus();
        }
    }
}
