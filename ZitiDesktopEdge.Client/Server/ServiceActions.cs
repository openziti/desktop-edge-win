using System.ServiceProcess;

using NLog;

namespace ZitiDesktopEdge.Server {
    public static class ServiceActions {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private static ServiceController sc = new ServiceController("ziti");
        public static string ServiceStatus() {
            var status = sc.Status;
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
