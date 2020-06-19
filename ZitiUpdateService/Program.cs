using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ZitiUpdateService {
	static class Program {
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main() {
			UpdateService updateSvc = new UpdateService();
#if DEBUG
			updateSvc.Debug();
			System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
#else
			ServiceBase[] ServicesToRun;
			ServicesToRun = new ServiceBase[]
			{
				updateSvc
			};
			ServiceBase.Run(ServicesToRun);
#endif
		}
	}
}
