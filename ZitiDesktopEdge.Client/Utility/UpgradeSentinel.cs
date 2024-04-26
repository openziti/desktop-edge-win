using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ZitiDesktopEdge.Utility {
	public class UpgradeSentinel {
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();
		public const string ZitiUpgradeSentinelExeName = "ZitiUpgradeSentinel.exe";
		private static string SentinelTempSource = Path.Combine(Path.GetTempPath(), ZitiUpgradeSentinelExeName);

		public static void StartUpgradeSentinel() {
			//start the sentinel process...
			using (Process process = new Process()) {
				string executablePath = Assembly.GetEntryAssembly().Location;
				string executableDirectory = Path.GetDirectoryName(executablePath);
				var sentinelSource = Path.Combine(executableDirectory, ZitiUpgradeSentinelExeName);

				if (File.Exists(sentinelSource)) {
					try {
						File.Copy(sentinelSource, SentinelTempSource, true);
						logger.Info("starting sentinel process: {}", SentinelTempSource);
						process.StartInfo.FileName = SentinelTempSource;
						process.StartInfo.Arguments = "version";
						process.StartInfo.RedirectStandardOutput = true;
						process.StartInfo.UseShellExecute = false;
						process.StartInfo.CreateNoWindow = true;
						process.Start();
					} catch (Exception ex) {
						logger.Error("cannot start sentinel service. {}", ex);
					}
				} else {
					logger.Warn("cannot start sentinel service. source file doesn't exist? {}", sentinelSource);
				}
			}
		}

		public static void RemoveUpgradeSentinelExe() {
			try {
				if (File.Exists(SentinelTempSource)) {
					// if the temp file exists, clear it out
					File.Delete(SentinelTempSource);
					logger.Debug("found and removed upgrade sentinel at: {}", SentinelTempSource);
				} else {
					logger.Debug("no upgrade sentinel exe at {} found to remove", SentinelTempSource);
				}
			} catch (Exception ex) {
				logger.Error($"OnStartup FAILED to delete the UpgradeSentinel at {SentinelTempSource}", ex);
			}
		}
	}
}
