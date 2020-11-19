using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using NLog;
using NLog.Config;
using NLog.Targets;

using ZitiDesktopEdge.Server;

namespace ZitiUpdateService {
	static class Program {

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main() {
			System.Environment.SetEnvironmentVariable("ZITI_EXTENDED_DEBUG", "true");
			var curdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string nlogFile = Path.Combine(curdir, "ziti-monitor-log.config");

			if (File.Exists(nlogFile)) {
				LogManager.Configuration = new XmlLoggingConfiguration(nlogFile);
			} else {
				var config = new LoggingConfiguration();
				var logname = "ziti-montior";
				// Targets where to log to: File and Console
				var logfile = new FileTarget("logfile") {
					FileName = $"{logname}.log",
					ArchiveEvery = FileArchivePeriod.Day,
					ArchiveNumbering = ArchiveNumberingMode.Rolling,
					MaxArchiveFiles = 7,
					Layout = "${longdate}|${level:uppercase=true:padding=5}|${logger}|${message}",
					//ArchiveAboveSize = 10000,
			};
				var logconsole = new ConsoleTarget("logconsole");

				// Rules for mapping loggers to targets            
				config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);
				config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

				// Apply config           
				LogManager.Configuration = config;
			}
			Logger.Info("service started - logger initialized");

			IPCServer svr = new IPCServer();
			Task ipcServer = svr.startIpcServer();
			Task eventServer = svr.startEventsServer();

			Task.WaitAll(ipcServer, eventServer);
			UpdateService updateSvc = new UpdateService();
			updateSvc.AutoLog = true;
#if DEBUG
			Logger.Error("================================");
			updateSvc.Debug();
			System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
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
