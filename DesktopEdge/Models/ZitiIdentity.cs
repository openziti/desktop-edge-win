using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
	public class ZitiIdentity {
		public List<ZitiService> Services { get; set; }
		public string Name { get; set; }
		public string ControllerUrl { get; set; }
		public bool IsEnabled { get; set; }
		public string EnrollmentStatus { get; set; }
		public string Status { get; set; }

		public ZitiIdentity()
		{
			//default constructor to support named initialization
			this.Services = new List<ZitiService>();
		}

		public ZitiIdentity(string Name, string ControllerUrl, bool IsEnabled, List<ZitiService> Services) {
			this.Name = Name;
			this.Services = Services;
			this.ControllerUrl = ControllerUrl;
			this.IsEnabled = IsEnabled;
			this.EnrollmentStatus = "Enrolled";
			this.Status = "Available";
		}

		public string Fingerprint { get; set; }

		public static ZitiIdentity FromClient(ServiceClient.Identity id)
		{
			ZitiIdentity zid = new ZitiIdentity()
			{
				ControllerUrl = id.Config.ztAPI,
				EnrollmentStatus = "status",
				Fingerprint = id.FingerPrint,
				IsEnabled = id.Active,
				Name = id.Name,
				Status = id.Status,
			};

			if (id.Services != null) {
				foreach (var svc in id.Services) {
					var zsvc = new ZitiService(svc);
					zid.Services.Add(zsvc);
				}
			}
			return zid;
		}
	}
}
