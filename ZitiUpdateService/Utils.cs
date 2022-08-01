using System;
using System.Security.AccessControl;
using System.Security.Principal;
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
using System.IO.Compression;
using Newtonsoft.Json;

namespace ZitiUpdateService {
    public static class AccessUtils {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static void GrantAccessToDirectory(string path) {
            try
            {
                if (!Directory.Exists(path)) {
                    return;
                }
                Logger.Debug("granting access permissions to folder: {path}", path);
                DirectorySecurity sec = Directory.GetAccessControl(path);
                // Using this instead of the "Everyone" string means we work on non-English systems.
                SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Modify | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(path, sec);
            } catch(Exception e) {
                Logger.Error(e, "Unexpected error when setting directory security: {0}", e.Message);
            }
        }

        public static void GrantAccessToFile(string path) {
            try {
                if (!File.Exists(path)) {
                    return;
                }
                Logger.Debug("granting access permissions to file: {path}", path);
                FileSecurity sec = File.GetAccessControl(path);
                // Using this instead of the "Everyone" string means we work on non-English systems.
                SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Modify | FileSystemRights.Delete | FileSystemRights.Synchronize, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
                File.SetAccessControl(path, sec);
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error when setting directory security: {0}", e.Message);
            }
        }
    }
}
