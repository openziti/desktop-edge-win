using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
    public class ZitiService {
		public string Name { get; set; }
		public string Url { get; set; }
		public ZitiService(string Name, string hostname, int port) {
			this.Name = Name;
			this.Url = hostname + ":" + port;
		}
	}
}
