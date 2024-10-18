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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
    public class ZitiIdentity {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public List<ZitiService> Services { get; set; }
        public string Name { get; set; }
        public string ControllerUrl { get; set; }

        public string ContollerVersion { get; set; }
        public bool IsEnabled { get; set; }
        public string EnrollmentStatus { get; set; }
        public string Status { get; set; }
        public bool IsMFAEnabled { get; set; }

        public void MFADebug(string where) {
            logger.Info($"{where}\n\tIdentifiter  : {Identifier}\n\tIsMFAEnabled : {IsMFAEnabled}\n\tIsMFANeeded  : {IsMFANeeded}\n\tNeedsExtAuth : {NeedsExtAuth}");
        }

        private bool mfaNeeded = false;
        public bool IsMFANeeded {
            get { return mfaNeeded; }
            set {
                mfaNeeded = value;
                if (!mfaNeeded) {
                    IsTimingOut = false;
                    IsTimedOut = false;
                    WasFullNotified = false;
                    WasNotified = false;
                }
            }
        }
        public int MinTimeout { get; set; }
        public int MaxTimeout { get; set; }
        public DateTime LastUpdatedTime { get; set; }
        public string TimeoutMessage { get; set; }
        public bool WasNotified { get; set; }
        public bool WasFullNotified { get; set; }
        public string Fingerprint { get; set; }
        public string Identifier { get; set; }
        private bool isTimedOut = false;

        public SemaphoreSlim Mutex { get; } = new SemaphoreSlim(1);
        public bool IsTimedOut {
            get { return isTimedOut; }
            set {
                isTimedOut = value;
                WasFullNotified = false;
            }
        }
        public string[] RecoveryCodes { get; set; }
        public bool IsTimingOut { get; set; }
        public bool IsConnected { get; set; }


        private bool svcFailingPostureCheck = false;
        public bool HasServiceFailingPostureCheck {
            get {
                return svcFailingPostureCheck;
            }
            set {
                logger.Info("Identity: {0} posture change. is a posture check failing: {1}", Name, !value);
                svcFailingPostureCheck = value;
                if (!value) {
                    IsMFANeeded = true;
                }
            }
        }

        public bool NeedsExtAuth { get; set; }

        /// <summary>
        /// Default constructor to support named initialization
        /// </summary>
        public ZitiIdentity() {
            this.IsConnected = true;
            this.Services = new List<ZitiService>();
        }

        public ZitiIdentity(string Name, string ControllerUrl, bool IsEnabled, List<ZitiService> Services) {
            this.Name = Name;
            this.Services = Services;
            this.ControllerUrl = ControllerUrl;
            this.IsEnabled = IsEnabled;
            this.EnrollmentStatus = "Enrolled";
            this.Status = "Available";
            this.MaxTimeout = -1;
            this.MinTimeout = -1;
            this.LastUpdatedTime = DateTime.Now;
            this.TimeoutMessage = "";
            this.RecoveryCodes = new string[0];
            this.IsTimingOut = false;
            this.IsTimedOut = false;
            this.IsConnected = true;
        }

        public static ZitiIdentity FromClient(DataStructures.Identity id) {
            ZitiIdentity zid = new ZitiIdentity() {
                ControllerUrl = (id.Config == null) ? "" : id.Config.ztAPI,
                ContollerVersion = id.ControllerVersion,
                EnrollmentStatus = "status",
                Fingerprint = id.FingerPrint,
                Identifier = id.Identifier,
                IsEnabled = id.Active,
                Name = (string.IsNullOrEmpty(id.Name) ? id.FingerPrint : id.Name),
                Status = id.Status,
                RecoveryCodes = new string[0],
                IsMFAEnabled = id.MfaEnabled,
                IsMFANeeded = id.MfaNeeded,
                IsTimedOut = false,
                IsTimingOut = false,
                MinTimeout = id.MinTimeout,
                MaxTimeout = id.MaxTimeout,
                LastUpdatedTime = id.MfaLastUpdatedTime,
                TimeoutMessage = "",
                IsConnected = true,
                NeedsExtAuth = id.NeedsExtAuth,
            };

            if (zid.Name.Contains(@"\")) {
                int pos = zid.Name.LastIndexOf(@"\");
                zid.Name = zid.Name.Substring(pos + 1);
            }

#if DEBUG
            zid.MFADebug("002");
#endif
            if (id.Services != null) {
                foreach (var svc in id.Services) {
                    if (svc != null) {
                        var zsvc = new ZitiService(svc);
                        zsvc.TimeUpdated = zid.LastUpdatedTime;
                        zid.Services.Add(zsvc);
                    }
                }
                zid.HasServiceFailingPostureCheck = zid.Services.Any(p => !p.HasFailingPostureCheck());
            }
            logger.Info("Identity: {0} updated To {1}", zid.Name, Newtonsoft.Json.JsonConvert.SerializeObject(id));
            return zid;
        }

        public void ShowMFAToast(string message) {
            logger.Info("Showing Notification from identity " + Name + " " + message + ".");
            new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddText(Name + " Service Access Warning")
                .AddText(message)
                .AddArgument("identifier", Identifier)
                .SetBackgroundActivation()
                .Show();
        }
    }
}
