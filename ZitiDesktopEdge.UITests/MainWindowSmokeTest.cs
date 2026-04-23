using System;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ZitiDesktopEdge.UITests {
	[TestClass]
	public class MainWindowSmokeTest {
		[TestMethod]
		public void MainWindow_Exists() {
			var app = Application.Launch(AppLauncher.ResolveExePath("Debug"));
			var automation = new UIA3Automation();
			Window? window = null;
			try {
				window = Retry.WhileNull(() =>
					automation.GetDesktop().FindFirstChild(
						cf => cf.ByControlType(ControlType.Window)
								.And(cf.ByProcessId(app.ProcessId))
								.And(cf.ByName("Ziti Desktop Edge")))?.AsWindow(),
					TimeSpan.FromSeconds(15)).Result;

				Assert.IsNotNull(window);
			} finally {
				window?.Close();
				var deadline = DateTime.UtcNow.AddSeconds(8);
				while (!app.HasExited && DateTime.UtcNow < deadline) Thread.Sleep(100);
				if (!app.HasExited) app.Kill();
				automation.Dispose();
			}
		}
	}
}
