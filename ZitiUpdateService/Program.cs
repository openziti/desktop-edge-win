/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using System;
using System.IO;
using System.Reflection;
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
            var asm = Assembly.GetExecutingAssembly();
            var logname = asm.GetName().Name;

            var curdir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string nlogFile = Path.Combine(curdir, $"{logname}-log.config");

            bool byFile = false;
            if (File.Exists(nlogFile)) {
                LogManager.Configuration = new XmlLoggingConfiguration(nlogFile);
                byFile = true;
            } else {
                var config = new LoggingConfiguration();
                // Targets where to log to: File and Console
                var logfile = new FileTarget("logfile") {
                    FileName = $"logs\\ZitiMonitorService\\{logname}.log",
                    ArchiveEvery = FileArchivePeriod.Day,
                    ArchiveNumbering = ArchiveNumberingMode.Rolling,
                    MaxArchiveFiles = 7,
                    AutoFlush = true,
                    Layout = "[${date:universalTime=true:format=yyyy-MM-ddTHH:mm:ss.fff}Z] ${level:uppercase=true:padding=5}\t${logger}\t${message}\t${exception:format=tostring}",
                };
                var logconsole = new ConsoleTarget("logconsole");

                // Rules for mapping loggers to targets            
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, logconsole);
                config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

                // Apply config           
                LogManager.Configuration = config;
            }
            Logger.Info("========================= ziti-monitor started =========================");
            Logger.Info("logger initialized");
            Logger.Info("    - version   : {0}", asm.GetName().Version.ToString());
            Logger.Info("    - using file: {0}", byFile);
            Logger.Info("    -       file: {0}", nlogFile);
            Logger.Info("========================================================================");

            UpdateService updateSvc = new UpdateService();
            updateSvc.AutoLog = true;
            try {
#if DEBUG
                bool nosvc = true;
                //bool nosvc = false;

                if (nosvc) {
                    Logger.Info("  - RUNNING AS DEBUG");
                    updateSvc.Debug();
                    updateSvc.WaitForCompletion();
                    Logger.Info("  - RUNNING AS DEBUG COMPLETE");
                } else {
                    ServiceBase[] ServicesToRun = new ServiceBase[]
                    {
                        updateSvc
                    };
                    Logger.Info("RUNNING AS DEBUG SERVICE");
                    ServiceBase.Run(ServicesToRun);
                }
#else
				ServiceBase[] ServicesToRun = new ServiceBase[]
				{
					updateSvc
				};
				ServiceBase.Run(ServicesToRun);
#endif
            } catch (Exception e) {
                Logger.Error("Unexpected exception: {0}", e);
            }
        }
    }
}
