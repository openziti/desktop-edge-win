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

using NLog;
using System;

namespace Ziti.Desktop.Edge.Models {
    public class ZDEWViewState {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool automaticUpdatesDisabled = false;
        private string updateUrl = "not set yet";
        public bool AutomaticUpdatesDisabled {
            get {
                return automaticUpdatesDisabled;
            }
            set {
                automaticUpdatesDisabled = value;
            }
        }
        public string AutomaticUpdateURL {
            get {
                return updateUrl;
            }
            set {
                updateUrl = value;
            }
        }

        public bool UpdateAvailable { get; set; }
        public UpdateInfo PendingUpdate { get; set; } = new UpdateInfo();

    }
    public class UpdateInfo {
        public DateTime InstallTime { get; set; } = DateTime.MinValue;
        public string Version { get; set; } = "0.0.0.0";

        public double TimeLeft {
            get {
                double timeLeft = (InstallTime - DateTime.Now).TotalSeconds;
                return timeLeft;
            }
        }
    }
}