using System.IO;
using System.Reflection;

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
			var asm = System.Reflection.Assembly.GetExecutingAssembly();
			var logname = asm.GetName().Name;

			var curdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string nlogFile = Path.Combine(curdir, "ziti-monitor-log.config");

			if (File.Exists(nlogFile)) {
				LogManager.Configuration = new XmlLoggingConfiguration(nlogFile);
			} else {
				var config = new LoggingConfiguration();
				// Targets where to log to: File and Console
				var logfile = new FileTarget("logfile") {
					FileName = $"logs\\ZitiMonitorService\\{logname}.log",
					ArchiveEvery = FileArchivePeriod.Day,
					ArchiveNumbering = ArchiveNumberingMode.Rolling,
					MaxArchiveFiles = 7,
					Layout = "${longdate}|${level:uppercase=true:padding=5}|${logger}|${message}",
			};
				var logconsole = new ConsoleTarget("logconsole");

				// Rules for mapping loggers to targets            
				config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
				config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

				// Apply config           
				LogManager.Configuration = config;
			}
			Logger.Info("service started - logger initialized");

			UpdateService updateSvc = new UpdateService();
			updateSvc.AutoLog = true;
#if DEBUG
			updateSvc.Debug();
			updateSvc.WaitForCompletion();
#else
			ServiceBase[] ServicesToRun = new ServiceBase[]
			{
				updateSvc
			};
			ServiceBase.Run(ServicesToRun);
#endif
		}
	}
}
