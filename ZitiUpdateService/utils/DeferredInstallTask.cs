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
using NLog;

namespace ZitiUpdateService.Utils {
    /// <summary>
    /// Manages a Windows Scheduled Task that runs a staged installer on next system startup.
    ///
    /// Used when <c>DeferInstallToRestart</c> is enabled: rather than installing immediately,
    /// the service registers this task and exits. On the next reboot the task fires as SYSTEM
    /// before any user session is active and the installer handles everything (stopping
    /// services, installing, restarting services) on its own.
    ///
    /// Task location in Task Scheduler:
    ///   Task Scheduler Library \ NetFoundry \ ZitiDesktopEdge-PendingUpdate
    ///
    /// Implemented via schtasks.exe to avoid a dependency on the Microsoft.Win32.TaskScheduler
    /// NuGet package (which would need to be added to the installer package).
    /// </summary>
    internal static class DeferredInstallTask {
        private const string TaskPath = @"NetFoundry\ZitiDesktopEdge-PendingUpdate";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Registers (or overwrites) the pending-install task for the given installer.
        /// The task runs once at next system startup as SYSTEM with highest privileges.
        /// </summary>
        internal static void Register(string installerPath) {
            Logger.Info("Registering deferred install task for: {0}", installerPath);
            // /F overwrites any existing task with the same name
            // /passive matches how installZDE launches the installer directly
            RunSchtasks($"/create /tn \"{TaskPath}\" /tr \"\\\"{installerPath}\\\" /passive\" /sc onstart /ru SYSTEM /rl HIGHEST /f");
            Logger.Info("Deferred install task registered successfully");
        }

        /// <summary>
        /// Removes the pending-install task if it exists. Call this after a successful
        /// install (on service startup) or when cancelling a deferred install.
        /// </summary>
        internal static void Remove() {
            try {
                RunSchtasks($"/delete /tn \"{TaskPath}\" /f");
                Logger.Info("Deferred install task removed");
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to remove deferred install task (may not exist)");
            }
        }

        /// <summary>
        /// Returns true if the pending-install task is currently registered in Task Scheduler.
        /// </summary>
        internal static bool IsRegistered() {
            try {
                int exit = RunSchtasks($"/query /tn \"{TaskPath}\"", throwOnError: false);
                return exit == 0;
            } catch (Exception ex) {
                Logger.Warn(ex, "Failed to check deferred install task registration");
                return false;
            }
        }

        private static void RunSchtasks(string args) {
            int exit = RunSchtasks(args, throwOnError: true);
            if (exit != 0) throw new Exception($"schtasks {args} exited with code {exit}");
        }

        private static int RunSchtasks(string args, bool throwOnError) {
            Logger.Debug("schtasks {0}", args);
            var psi = new ProcessStartInfo("schtasks.exe", args) {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            using (var p = Process.Start(psi)) {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrWhiteSpace(stdout)) Logger.Debug("schtasks stdout: {0}", stdout.Trim());
                if (!string.IsNullOrWhiteSpace(stderr)) Logger.Debug("schtasks stderr: {0}", stderr.Trim());
                if (throwOnError && p.ExitCode != 0) {
                    throw new Exception($"schtasks exited {p.ExitCode}: {stderr.Trim()}");
                }
                return p.ExitCode;
            }
        }
    }
}
