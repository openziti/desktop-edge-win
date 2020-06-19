using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.IO;

namespace ZitiTunneler.Models {
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
			return File.ReadAllText(_logDirectory);
		}
	}
}
