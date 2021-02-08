using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Utility {
    public static class VersionUtil {
		public static Version NormalizeVersion(Version v) {
			if (v.Minor < 1) return new Version(v.Major, 0, 0, 0);
			if (v.Build < 1) return new Version(v.Major, v.Minor, 0, 0);
			if (v.Revision < 1) return new Version(v.Major, v.Minor, v.Build, 0);
			return v;
		}
	}
}
