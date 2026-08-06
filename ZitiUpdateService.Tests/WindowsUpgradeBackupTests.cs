/*
    Copyright NetFoundry Inc.

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    https://www.apache.org/licenses/LICENSE-2.0
*/

using ZitiUpdateService.Utils;

namespace ZitiUpdateService.Tests {
    /// <summary>
    /// Ensure settings are restored after a Windows upgrade (issue #1050). Rationale is on <see cref="WindowsUpgradeBackup"/>.
    /// </summary>
    [TestClass]
    public class WindowsUpgradeBackupTests {

        private const string ModifiedSettingsJson = @"{
  ""AutomaticUpdatesDisabled"": false,
  ""AutomaticUpdateURL"": ""https://get.openziti.io/zdew/stable.json"",
  ""AlivenessChecksBeforeAction"": 12,
  ""DeferInstallToRestart"": false,
  ""MaintenanceWindowStart"": 0,
  ""MaintenanceWindowEnd"": 8,
  ""MaintenanceWindowFrequency"": 1,
  ""MaintenanceWindowDayOfWeek"": 2,
  ""MaintenanceWindowDayOfMonth"": null,
  ""MaintenanceWindowMonthlyMode"": 0,
  ""MaintenanceWindowMonthlyOrdinal"": null
}";

        private const string DefaultSettingsJson = @"{
  ""AutomaticUpdatesDisabled"": false,
  ""AutomaticUpdateURL"": ""https://get.openziti.io/zdew/stable.json"",
  ""AlivenessChecksBeforeAction"": 12,
  ""DeferInstallToRestart"": false,
  ""MaintenanceWindowStart"": 0,
  ""MaintenanceWindowEnd"": 0,
  ""MaintenanceWindowFrequency"": 0,
  ""MaintenanceWindowDayOfWeek"": null,
  ""MaintenanceWindowDayOfMonth"": null,
  ""MaintenanceWindowMonthlyMode"": 0,
  ""MaintenanceWindowMonthlyOrdinal"": null
}";

        private string root = null!;
        private string liveFolder = null!;
        private string oldFolder = null!;
        private string btFolder = null!;

        [TestInitialize]
        public void CreateFolders() {
            root = Path.Combine(Path.GetTempPath(), "zdew-upgrade-" + Guid.NewGuid().ToString("N"));
            liveFolder = Path.Combine(root, "live");
            oldFolder = Path.Combine(root, "Windows.old");
            btFolder = Path.Combine(root, "$Windows.~BT");
            Directory.CreateDirectory(liveFolder);
        }

        [TestCleanup]
        public void RemoveFolders() {
            Directory.Delete(root, true);
        }

        [TestMethod]
        public void MissingFile_RestoredFromBackupAndBackupRemoved() {
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, "settings.json"), ModifiedSettingsJson);

            WindowsUpgradeBackup.RestoreMissingFiles(liveFolder, new string[] { oldFolder, btFolder });

            Assert.AreEqual(ModifiedSettingsJson, File.ReadAllText(Path.Combine(liveFolder, "settings.json")));
            Assert.IsFalse(Directory.Exists(oldFolder), "emptied backup folder should be removed");
        }

        [TestMethod]
        public void ExistingFile_WinsAndBackupLeftInPlace() {
            File.WriteAllText(Path.Combine(liveFolder, "settings.json"), DefaultSettingsJson);
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, "settings.json"), ModifiedSettingsJson);

            WindowsUpgradeBackup.RestoreMissingFiles(liveFolder, new string[] { oldFolder, btFolder });

            Assert.AreEqual(DefaultSettingsJson, File.ReadAllText(Path.Combine(liveFolder, "settings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(oldFolder, "settings.json")), "unrestored backup must not be deleted");
        }

        [TestMethod]
        public void EmptyBackupFolder_Removed() {
            Directory.CreateDirectory(oldFolder);

            WindowsUpgradeBackup.RestoreMissingFiles(liveFolder, new string[] { oldFolder, btFolder });

            Assert.IsFalse(Directory.Exists(oldFolder));
        }

        [TestMethod]
        public void EmptyBackupFile_RejectedAndLeftInPlace() {
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(Path.Combine(oldFolder, "settings.json"), string.Empty);

            WindowsUpgradeBackup.RestoreMissingFiles(liveFolder, new string[] { oldFolder, btFolder });

            Assert.IsFalse(File.Exists(Path.Combine(liveFolder, "settings.json")), "empty backup must not be restored");
            Assert.IsTrue(File.Exists(Path.Combine(oldFolder, "settings.json")), "rejected backup must not be deleted");
        }

        [TestMethod]
        public void NoBackupFolders_LeavesLiveFolderUntouched() {
            WindowsUpgradeBackup.RestoreMissingFiles(liveFolder, new string[] { oldFolder, btFolder });

            Assert.AreEqual(0, Directory.GetFileSystemEntries(liveFolder).Length);
        }
    }
}
