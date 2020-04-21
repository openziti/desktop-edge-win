using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiTunneler.Models {
	public class ZitiIdentity {
		public List<ZitiService> Services { get; set; }
		public string Name { get; set; }
		public string ControllerUrl { get; set; }

		private bool isEnabled = false;
		public bool IsEnabled
		{
			get
			{
				return isEnabled;
			}
			set
			{
				this.isEnabled = value;
			}
		}
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
				IsEnabled = false,
				Name = id.Name,
				Status = id.Status,
			};

			if (id.Services != null)
			{
				foreach (var svc in id.Services)
				{
					var zsvc = new ZitiService(svc.Name, svc.HostName + ":" + svc.Port);
					zid.Services.Add(zsvc);
				}
			}
			return zid;
		}
	}
}
