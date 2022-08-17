using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Ziti.Desktop.Edge.Models
{
    public class ZDEWViewState
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool automaticUpdatesDisabled = false;
        public bool AutomaticUpdatesDisabled
        {
            get
            {
                return automaticUpdatesDisabled;
            }
            set
            {
                automaticUpdatesDisabled = value;
            }
        }

        public UpdateInfo PendingUpdate { get; set; } = new UpdateInfo();

        internal void AutomaticUpdatesEnabledFromString(string automaticUpgradeDisabled)
        {
            bool disabled = bool.TrueString.ToLower() == automaticUpgradeDisabled?.ToLower().Trim();
            this.AutomaticUpdatesDisabled = disabled;
        }
    }
    public class UpdateInfo
    {
        public DateTime InstallTime { get; set; } = DateTime.MinValue;
        public string Version { get; set; } = "0.0.0.0";

        public double TimeLeft
        {
            get
            {
                double timeLeft = (InstallTime - DateTime.Now).TotalSeconds;
                return timeLeft;
            }
        }
    }
}
