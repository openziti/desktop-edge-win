using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
	public class ZitiIdentity {
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();
		public List<ZitiService> Services { get; set; }
		public string Name { get; set; }
		public string ControllerUrl { get; set; }
		public bool IsEnabled { get; set; }
		public string EnrollmentStatus { get; set; }
		public string Status { get; set; }
		public bool IsMFAEnabled { get; set; }
		public int MinTimeout { get; set; }
		public int MaxTimeout { get; set; }
		public DateTime LastUpdatedTime { get; set; }
		public string TimeoutMessage { get; set; }
		public bool WasNotified { get; set; }
		public bool WasFullNotified { get; set; }
		public string Fingerprint { get; set; }
		public MFA MFAInfo { get; set; }

		private bool svcFailingPostureCheck = false;
		public bool HasServiceFailingPostureCheck {
			get {
				return svcFailingPostureCheck;
			}
			set {
				logger.Info("Identity: {0} posture change. is a posture check failing: {1}", Name, !value);
				svcFailingPostureCheck = value;
			}
		}

		/// <summary>
		/// Default constructor to support named initialization
		/// </summary>
		public ZitiIdentity() {
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
		}

		public static ZitiIdentity FromClient(DataStructures.Identity id) {
			ZitiIdentity zid = new ZitiIdentity() {
				ControllerUrl = id.Config.ztAPI + " at " + id.ControllerVersion,
				EnrollmentStatus = "status",
				Fingerprint = id.FingerPrint,
				IsEnabled = id.Active,
				Name = (string.IsNullOrEmpty(id.Name) ? id.FingerPrint : id.Name),
				Status = id.Status,
				IsMFAEnabled = id.MfaEnabled,
				MFAInfo = new MFA() {
					IsAuthenticated = !id.MfaNeeded,
				},
				MinTimeout = id.MinTimeout,
				MaxTimeout = id.MaxTimeout,
				LastUpdatedTime = id.MfaLastUpdatedTime,
				TimeoutMessage = ""
			};


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
	}
}
