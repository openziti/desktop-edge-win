using System;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ZitiDesktopEdge.UITests {
	[TestClass]
	public class MainWindowSmokeTest {
		private Application? _app;
		private UIA3Automation? _automation;
		private ConditionFactory? _cf;
		private Window? _window;

		[TestInitialize]
		public void LaunchApp() {
			_app = Application.Launch(AppLauncher.ResolveExePath("Debug"));
			_automation = new UIA3Automation();
			_cf = _automation.ConditionFactory;

			ConditionBase windowCondition = _cf.ByAutomationId("MainUI").And(_cf.ByProcessId(_app.ProcessId));
			AutomationElement desktop = _automation.GetDesktop();
			DateTime windowDeadline = DateTime.UtcNow.AddSeconds(15);
			Window? window = null;
			while (window == null && DateTime.UtcNow < windowDeadline) {
				AutomationElement? candidate = desktop.FindFirstChild(windowCondition);
				if (candidate != null) window = candidate.AsWindow();
				else Thread.Sleep(100);
			}
			Assert.IsNotNull(window, "Main window did not appear within 15 seconds");
			_window = window;
			_window.Focus();
		}

		[TestCleanup]
		public void CloseApp() {
			_window?.Close();
			if (_app != null) {
				DateTime exitDeadline = DateTime.UtcNow.AddSeconds(8);
				while (!_app.HasExited && DateTime.UtcNow < exitDeadline) Thread.Sleep(100);
				if (!_app.HasExited) _app.Kill();
			}
			_automation?.Dispose();
		}

		[TestMethod]
		public void MainWindow_Opens() {
			Assert.IsNotNull(_window);
		}

		[TestMethod]
		public void ConnectLabel_IsPresent() {
			Window window = _window!;
			ConditionFactory cf = _cf!;

			AutomationElement? label = window.FindFirstDescendant(cf.ByAutomationId("ConnectLabel"));
			Assert.IsNotNull(label, "ConnectLabel not found");
		}

		[TestMethod]
		public void MainMenu_OpensWithAllExpectedItems() {
			Window window = _window!;
			ConditionFactory cf = _cf!;

			AutomationElement? menuButton = null;
			DateTime buttonDeadline = DateTime.UtcNow.AddSeconds(5);
			while (menuButton == null && DateTime.UtcNow < buttonDeadline) {
				menuButton = window.FindFirstDescendant(cf.ByAutomationId("MainMenuButton"));
				if (menuButton == null) Thread.Sleep(100);
			}
			Assert.IsNotNull(menuButton, "MainMenuButton not found");
			menuButton.Click();

			AutomationElement? mainMenu = null;
			DateTime menuDeadline = DateTime.UtcNow.AddSeconds(5);
			while (mainMenu == null && DateTime.UtcNow < menuDeadline) {
				mainMenu = window.FindFirstDescendant(cf.ByAutomationId("MainMenu"));
				if (mainMenu == null) Thread.Sleep(100);
			}
			Assert.IsNotNull(mainMenu, "MainMenu did not open");

			AutomationElement? identities = window.FindFirstDescendant(cf.ByAutomationId("IdentitiesButton"));
			Assert.IsNotNull(identities, "IdentitiesButton not found");

			AutomationElement? advanced = window.FindFirstDescendant(cf.ByAutomationId("AdvancedSettings"));
			Assert.IsNotNull(advanced, "AdvancedSettings not found");

			AutomationElement? about = window.FindFirstDescendant(cf.ByAutomationId("About"));
			Assert.IsNotNull(about, "About not found");

			AutomationElement? feedback = window.FindFirstDescendant(cf.ByAutomationId("Feedback"));
			Assert.IsNotNull(feedback, "Feedback not found");

			AutomationElement? support = window.FindFirstDescendant(cf.ByAutomationId("Support"));
			Assert.IsNotNull(support, "Support not found");

			AutomationElement? detach = window.FindFirstDescendant(cf.ByAutomationId("DetachButton"));
			Assert.IsNotNull(detach, "DetachButton not found");
		}
	}
}
