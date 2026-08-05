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
using System.IO;
using NLog;

namespace ZitiUpdateService.Utils {
    // A Windows upgrade moves the SYSTEM profile into a backup folder. ziti-edge-tunnel only
    // recovers its own files, and this service regenerates default settings at startup before
    // ziti-edge-tunnel even runs, so this service restores its own folder before Load()/Write().
    public static class WindowsUpgradeBackup {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static string[] BackupFolders(string folder) {
            string root = Path.GetPathRoot(folder);
            string relativePath = folder.Substring(root.Length);
            return new string[] {
                Path.Combine(root, "Windows.old", relativePath),
                Path.Combine(root, "$Windows.~BT", relativePath)
            };
        }

        public static void RestoreMissingFiles(string folder, string[] backupFolders) {
            foreach (string backupFolder in backupFolders) {
                if (!Directory.Exists(backupFolder)) {
                    continue;
                }
                foreach (string backupFile in Directory.GetFiles(backupFolder)) {
                    string liveFile = Path.Combine(folder, Path.GetFileName(backupFile));
                    if (File.Exists(liveFile)) {
                        // never delete a backup that was not restored, Windows prunes Windows.old on its own
                        Logger.Warn("keeping existing file {0}, backup file left at {1}", liveFile, backupFile);
                        continue;
                    }
                    try {
                        File.Copy(backupFile, liveFile);
                        Logger.Info("restored {0} from the Windows upgrade backup at {1}", liveFile, backupFile);
                    } catch (Exception ex) {
                        Logger.Error("failed to copy backup file {0}: {1}", backupFile, ex.Message);
                        continue;
                    }
                    try {
                        File.Delete(backupFile);
                    } catch (Exception ex) {
                        Logger.Warn("failed to remove backup file {0}: {1}", backupFile, ex.Message);
                    }
                }
                if (Directory.GetFileSystemEntries(backupFolder).Length == 0) {
                    try {
                        Directory.Delete(backupFolder);
                    } catch (Exception ex) {
                        Logger.Warn("failed to remove empty backup folder {0}: {1}", backupFolder, ex.Message);
                    }
                }
            }
        }
    }
}
