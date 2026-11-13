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
            try {
                if (!Directory.Exists(path)) {
                    return;
                }
                Logger.Debug("granting access permissions to folder: {path}", path);
                DirectorySecurity sec = Directory.GetAccessControl(path);
                // Using this instead of the "Everyone" string means we work on non-English systems.
                SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.Modify | FileSystemRights.Synchronize, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(path, sec);
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error when setting directory security: {0}", e.Message);
            }
        }

        /// <summary>
        /// Drops any explicit Authenticated Users rule from a file and turns inheritance back on, so
        /// the file goes back to the Program Files default (admins only).
        ///
        /// Older versions granted Authenticated Users Modify on the service's .config / NLog files.
        /// Not re-granting isn't enough -- the ACE is already on disk from a prior run -- so run this at
        /// startup to clean up machines provisioned by an older build.
        /// </summary>
        public static void RemoveAuthenticatedUsersAccess(string path) {
            try {
                if (!File.Exists(path)) {
                    return;
                }
                FileSecurity sec = File.GetAccessControl(path);
                SecurityIdentifier authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                sec.PurgeAccessRules(authenticatedUsers);
                // false = unprotect, so it inherits the parent DACL again
                sec.SetAccessRuleProtection(false, true);
                File.SetAccessControl(path, sec);
                Logger.Debug("removed Authenticated Users access from file: {0}", path);
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error when tightening file security on {0}: {1}", path, e.Message);
            }
        }
    }
}
