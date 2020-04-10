using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiTunneler.Models {
	public class ZitiIdentity {
		public ZitiService[] Services { get; set; }
		public string Name { get; set; }
		public string ControllerUrl { get; set; }
		public bool IsEnabled { get; set; }
		public ZitiIdentity(string Name, string ControllerUrl, bool IsEnabled, ZitiService[] Services) {
			this.Name = Name;
			this.Services = Services;
			this.ControllerUrl = ControllerUrl;
			this.IsEnabled = IsEnabled;
		}
	}
}
