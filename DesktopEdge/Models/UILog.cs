using System;
using System.IO;

namespace ZitiDesktopEdge.Models {
	public class UILog {

		private static string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetFoundry" ,"Logs", "Log.log");
		public static void Log(string message) {
			try {
				File.WriteAllText(_logDirectory, message);
			} catch (Exception e) {
				Console.WriteLine(e.Message);
			}
		}

		public static string GetLogs() {
			string logs = "";
			if (File.Exists(_logDirectory)) {
				logs = File.ReadAllText(_logDirectory);
			}
			return logs;
		}
	}
}
