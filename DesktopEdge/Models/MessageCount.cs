using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
	public class MessageCount {

		public int Total { get; set; }
		public string Message { get; set; }
		public override string ToString() {
			return Total + " " + Message;
		}
	}
}
