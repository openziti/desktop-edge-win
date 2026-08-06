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
    // A Windows upgrade can move the SYSTEM profile into a backup folder.
    // Restore settings from the backup folder so defaults aren't written when the service starts.
    public static class WindowsUpgradeBackup {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Where Windows Setup backs up 'destination': the system drive's Windows.old plus the drive-relative path.
        public static string[] BackupFolders(string destination) {
            string systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string relativePath = destination.Substring(Path.GetPathRoot(destination).Length);
            return new string[] {
                Path.Combine(systemDrive, "Windows.old", relativePath),
                Path.Combine(systemDrive, "$Windows.~BT", relativePath)
            };
        }

        // Copies backup files into 'destination', skipping any file that already exists there and empty backups.
        // A backup file is deleted only after a successful copy, its folder only once empty.
        public static void RestoreMissingFiles(string destination, string[] backupFolders) {
            foreach (string backupFolder in backupFolders) {
                try {
                    RestoreFolder(destination, backupFolder);
                } catch (Exception ex) {
                    // Runs in a static initializer, an escaped exception would kill the service.
                    Logger.Error(ex, "failed restoring backup files from {0}", backupFolder);
                }
            }
        }

        private static void RestoreFolder(string destination, string backupFolder) {
            if (!Directory.Exists(backupFolder)) {
                return;
            }
            foreach (string backupFile in Directory.GetFiles(backupFolder)) {
                string liveFile = Path.Combine(destination, Path.GetFileName(backupFile));
                if (File.Exists(liveFile)) {
                    Logger.Warn("keeping existing file {0}, backup file left at {1}", liveFile, backupFile);
                    continue;
                }
                if (new FileInfo(backupFile).Length == 0) {
                    Logger.Warn("ignoring empty backup file {0}", backupFile);
                    continue;
                }
                try {
                    File.Copy(backupFile, liveFile);
                    Logger.Info("restored {0} from the Windows upgrade backup at {1}", liveFile, backupFile);
                } catch (Exception ex) {
                    Logger.Error(ex, "failed to copy backup file {0}", backupFile);
                    continue;
                }
                try {
                    File.Delete(backupFile);
                } catch (Exception ex) {
                    Logger.Warn(ex, "failed to remove backup file {0}", backupFile);
                }
            }
            if (Directory.GetFileSystemEntries(backupFolder).Length == 0) {
                try {
                    Directory.Delete(backupFolder);
                } catch (Exception ex) {
                    Logger.Warn(ex, "failed to remove empty backup folder {0}", backupFolder);
                }
            }
        }
    }
}
