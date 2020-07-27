using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
    public class ZitiService {
		public string Name { get; set; }
		public string AssignedIP { get; set; }
		public string Host { get; set; }
		public string Port { get; set; }
		public bool OwnsIntercept { get; set; }
		public string Url
		{
			get
			{
				if (this.OwnsIntercept)
				{
					return Host + ":" + Port;
				} else
				{
					return AssignedIP + ":" + Port;
				}
			}
		}
		public string Warning
		{
			get
			{
				if (this.OwnsIntercept)
				{
					return "";
				}
				else
				{
					return $"Another identity already mapped the specified hostname: {Host}.\nThis service is only available via IP";
				}
			}
		}

		public ZitiService() {
		}

		public ZitiService(ZitiDesktopEdge.ServiceClient.Service svc)
		{
			this.Name = svc.Name;
			this.AssignedIP = svc.AssignedIP;
			this.Host = svc.InterceptHost;
			this.Port = svc.InterceptPort.ToString();
			this.OwnsIntercept = svc.OwnsIntercept;
		}
	}
}
