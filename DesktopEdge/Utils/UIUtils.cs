using System;
using System.Threading.Tasks;

using NLog;

namespace Ziti.Desktop.Edge.Utils {
    public class UIUtils {

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		public static void SetLogLevel(string level) {
			try
			{
				Logger.Info("request to change log level received: {0}", level);
				if ((""+level).ToLower() == "verbose") {
					level = "trace";
					Logger.Info("request to change log level to verbose - but using trace instead");
				}
				var l = LogLevel.FromString(level);
				foreach (var rule in LogManager.Configuration.LoggingRules) {
					rule.EnableLoggingForLevel(l);
					rule.SetLoggingLevels(l, LogLevel.Fatal);
				}

				LogManager.ReconfigExistingLoggers();
				Logger.Info("logger reconfigured to log at level: {0}", l);
			} catch (Exception e) {
				Logger.Error(e, "Failed to set log level: {0}", e.Message);
			}
		}
	}
}
