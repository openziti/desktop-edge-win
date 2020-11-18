using System.ServiceProcess;

using NLog;

namespace ZitiDesktopEdge.Server {
    internal static class ServiceActions {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        
        private static ServiceController sc = new ServiceController("ziti");
        public static string ServiceStatus() {
            var status = sc.Status;
            return status.ToString();
        }

        public static string StartService() {
            Logger.Info("request to start ziti service received... processing...");
            sc.Start();
            Logger.Info("request to start ziti service received... complete...");
            return ServiceStatus();
        }

        public static string StopService() {
            Logger.Info("request to stop ziti service received... processing...");
            sc.Start();
            Logger.Info("request to stop ziti service received... complete...");
            return ServiceStatus();
        }
    }
}
