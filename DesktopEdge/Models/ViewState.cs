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