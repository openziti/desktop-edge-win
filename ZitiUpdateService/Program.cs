using System;
using System.ServiceProcess;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ZitiUpdateService {
	static class Program {
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main() {
			//Environment.SetEnvironmentVariable("ZITI_EXTENDED_DEBUG", "true");

			var config = new LoggingConfiguration();
			var logname = "ziti-montior";
			// Targets where to log to: File and Console
			var logfile = new FileTarget("logfile") { 
				FileName = $"{logname}.log",
				ArchiveEvery = FileArchivePeriod.Day,
				ArchiveNumbering = ArchiveNumberingMode.Rolling,
				MaxArchiveFiles = 7,
				//ArchiveAboveSize = 10000,
			};
			var logconsole = new ConsoleTarget("logconsole");

			// Rules for mapping loggers to targets            
			config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
			config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

			// Apply config           
			LogManager.Configuration = config;

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
