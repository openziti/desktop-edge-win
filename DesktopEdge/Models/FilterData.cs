using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Models {
	public class FilterData {

		public FilterData():this("", "", "asc") { }

		public FilterData(string searchFor, string sortBy, string sortHow) {
			SearchFor = searchFor;
			SortBy = sortBy;
			SortHow = sortHow;
		}

		public string SearchFor { get; set; }
		public string SortBy { get; set; }
		public string SortHow { get; set; }
	}
}
