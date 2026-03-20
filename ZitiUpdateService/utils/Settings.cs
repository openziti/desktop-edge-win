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
using System.Threading;
using Newtonsoft.Json;
using NLog;
using ZitiDesktopEdge.DataStructures;

namespace ZitiUpdateService.Utils {
    internal class Settings {
        const int DefaultAlivenessChecks = 12;//12 checks == ~60s

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private FileSystemWatcher watcher;

        [JsonIgnore]
        private string Location { get; set; }

        public bool AutomaticUpdatesDisabled { get; set; }
        public string AutomaticUpdateURL { get; set; }
        public int? AlivenessChecksBeforeAction { get; set; } // the number of times the aliveness check can fail before terminating the tunneler

        public event System.EventHandler<ControllerEvent> OnConfigurationChange;

        internal Settings(bool doInit) {
            if (doInit) {
                init();
            }
            AlivenessChecksBeforeAction = DefaultAlivenessChecks;
        }

        public Settings() {
        }

        private void init() {
            string folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "NetFoundry", "ZitiUpdateService");
            string file = "settings.json";
            Location = Path.Combine(folder, file);
            Directory.CreateDirectory(folder);
            watcher = new FileSystemWatcher(folder);
            watcher.Filter = file;

            watcher.NotifyFilter = NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }

        private static JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.Indented };
        internal void Load() {
            try {
                string json = File.ReadAllText(Location);
                var jsonReaderEvt = new JsonTextReader(new StringReader(json));
                Settings s = serializer.Deserialize<Settings>(jsonReaderEvt);
                if (s != null) {
                    Update(s);
                } else {
                    Logger.Debug("settings file was null? file doesn't exist or file was garbage?");
                }
            } catch (Exception ex) {
                // probably means the file doesn't exist or hasn't finished being written yet
                throw ex;
            }
        }
        internal void Write() {
            lock (this) {
                this.watcher.Changed -= OnChanged;
                try {
                    using (StreamWriter file = File.CreateText(Location)) {
                        serializer.Serialize(file, this);
                        file.Flush();
                        file.Close();
                    }
                    this.OnConfigurationChange?.Invoke(null, null);
                } catch {
                    // do nothing
                }
                this.watcher.Changed += OnChanged;
            }
        }


        private static void OnError(object sender, ErrorEventArgs e) {
        }

        private void OnRenamed(object sender, RenamedEventArgs e) {
            Logger.Info("Settings file renamed. Resetting to defaults...");
            this.Update(new Settings());
        }

        private void OnDeleted(object sender, FileSystemEventArgs e) {
            Logger.Info("Settings file deleted. Resetting to defaults...");
            this.Update(new Settings());
        }

        private void OnCreated(object sender, FileSystemEventArgs e) {
        }

        private void OnChanged(object sender, FileSystemEventArgs e) {
            try {
                this.Load();
                this.OnConfigurationChange?.Invoke(null, null);
            } catch (IOException ioe) {
                int isLocked = (int)(System.Runtime.InteropServices.Marshal.GetHRForException(ioe) & 0xFFFF);
                if (isLocked == 32) {
                    /*
                     see https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
                     ERROR_SHARING_VIOLATION
                     32 (0x20)
                     The process cannot access the file because it is being used by another process.
                    */
                    Thread.Sleep(500); //wait 500ms for the file to finish being written
                    try {
                        Load();
                        this.OnConfigurationChange?.Invoke(null, null);
                    } catch (Exception ex2) {
                        Logger.Debug("unexpected error loading settings twice. not trying again. {0}", ex2);
                    }
                } else {
                    Logger.Debug("unexpected error loading settings. file was null? file doesn't exist or file was garbage? {0}", ioe);
                }
            } catch (Exception ex) {
                Logger.Debug("unexpected error loading settings. file was null? file doesn't exist or file was garbage? {0}", ex);
            }
        }

        private void Update(Settings source) {
            this.AutomaticUpdatesDisabled = source.AutomaticUpdatesDisabled;
            this.AutomaticUpdateURL = source.AutomaticUpdateURL;
            if (source.AlivenessChecksBeforeAction != null) {
                AlivenessChecksBeforeAction = source.AlivenessChecksBeforeAction;
            } else {
                AlivenessChecksBeforeAction = DefaultAlivenessChecks;
            }
        }
    }
}
