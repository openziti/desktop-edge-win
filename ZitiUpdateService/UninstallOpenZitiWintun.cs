using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

using NLog;

namespace ZitiUpdateService {
    internal class UninstallOpenZitiWintun {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public static void DetectAndUninstallOpenZitiWintun() {
            Task.Run(() => {
                Logger.Debug("Checking to see if the legacy OpenZitiWintunInstaller is installed");
                try {
                    bool r = UninstallOpenZitiWintunInstaller();
                    if (r) {
                        Logger.Info("removed legacy OpenZitiWintunInstaller");
                    } else {
                        Logger.Debug("OpenZitiWintunInstaller is not installed");
                    }
                } catch(Exception ex) {
                    Logger.Error(ex, "There was a problem invoking UninstallOpenZitiWintunInstaller!");
                }
            });
        }

        private static bool UninstallOpenZitiWintunInstaller() {
            ManagementObjectSearcher mos = new ManagementObjectSearcher("SELECT * FROM Win32_Product WHERE Name = 'OpenZitiWintunInstaller'");
            foreach (ManagementObject mo in mos.Get()) {
                var hr = mo.InvokeMethod("Uninstall", null);
                return hr != null;
            }
            //was not found...
            return false;
        }
    }
}
