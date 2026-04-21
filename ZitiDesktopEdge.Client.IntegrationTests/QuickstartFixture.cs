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

using System.Diagnostics;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ZitiDesktopEdge.Client.IntegrationTests;

/// <summary>
/// Assembly-wide setup: spins up a fresh local ziti quickstart and provisions
/// the standard test identities by invoking scripts/setup-ids-for-test.ps1.
///
/// Tests that depend on the quickstart must call <see cref="SkipIfUnavailable"/>
/// so the suite runs cleanly on machines that don't have ziti.exe or the
/// ziti-edge-tunnel service installed.
/// </summary>
[TestClass]
public static class QuickstartFixture {
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private const string ControllerUrlDefault = "https://localhost:1280";
	private const string RouterNameDefault = "quickstart-router";
	private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(5);

	public static string? ZitiHome { get; private set; }
	public static string? IdentityDir { get; private set; }
	public static string ControllerUrl { get; private set; } = ControllerUrlDefault;
	public static string? SetupFailureReason { get; private set; }

	[AssemblyInitialize]
	public static void SetUp(TestContext context) {
		ConfigureNLog();

		string? zitiExe = FindExecutableOnPath("ziti.exe");
		if (zitiExe is null) {
			SetupFailureReason = "ziti.exe not found on PATH; integration tests will be skipped.";
			Logger.Warn(SetupFailureReason);
			return;
		}

		string scriptPath = LocateSetupScript();
		string pwsh = FindExecutableOnPath("pwsh.exe")
			?? throw new FileNotFoundException("pwsh.exe (PowerShell 7+) is required on PATH.");

		string tempHome = Path.Combine(Path.GetTempPath(),
			"zdew-integration-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempHome);

		try {
			RunSetupScript(pwsh, scriptPath, tempHome);
			ZitiHome = tempHome;
			IdentityDir = Path.Combine(tempHome, "identities");
			Logger.Info("Quickstart setup complete. ZitiHome={0}", ZitiHome);
		} catch (Exception ex) {
			SetupFailureReason = "Quickstart setup failed: " + ex.Message;
			Logger.Error(ex, SetupFailureReason);
			TryDelete(tempHome);
		}
	}

	[AssemblyCleanup]
	public static void TearDown() {
		try {
			Process.Start(new ProcessStartInfo("taskkill", "/F /IM ziti.exe") {
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			})?.WaitForExit(10_000);
		} catch (Exception ex) {
			Logger.Warn(ex, "failed to kill ziti.exe during teardown");
		}

		if (ZitiHome is not null) {
			TryDelete(ZitiHome);
		}
	}

	/// <summary>
	/// Aborts the current test as Inconclusive if the fixture couldn't set up
	/// a working quickstart (e.g. ziti.exe missing, script failed).
	/// </summary>
	public static void SkipIfUnavailable() {
		if (SetupFailureReason is not null) {
			Assert.Inconclusive(SetupFailureReason);
		}
	}

	private static void RunSetupScript(string pwsh, string scriptPath, string tempHome) {
		var args = new[] {
			"-NoProfile",
			"-ExecutionPolicy", "Bypass",
			"-File", scriptPath,
			"-ClearIdentitiesOk",
			"-NonInteractive",
			"-ZitiHome", tempHome,
			"-Normal",
		};

		var psi = new ProcessStartInfo(pwsh) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
		};
		foreach (string arg in args) {
			psi.ArgumentList.Add(arg);
		}

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException("failed to launch " + pwsh);

		process.OutputDataReceived += (_, e) => { if (e.Data is not null) Logger.Info("[setup] {0}", e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Logger.Warn("[setup] {0}", e.Data); };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		if (!process.WaitForExit((int)SetupTimeout.TotalMilliseconds)) {
			try { process.Kill(entireProcessTree: true); } catch { }
			throw new TimeoutException($"setup-ids-for-test.ps1 did not complete within {SetupTimeout}.");
		}

		if (process.ExitCode != 0) {
			throw new InvalidOperationException(
				$"setup-ids-for-test.ps1 exited with code {process.ExitCode}.");
		}
	}

	private static string LocateSetupScript() {
		string start = AppContext.BaseDirectory;
		for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent) {
			string candidate = Path.Combine(dir.FullName, "scripts", "setup-ids-for-test.ps1");
			if (File.Exists(candidate)) {
				return candidate;
			}
		}
		throw new FileNotFoundException(
			"could not locate scripts/setup-ids-for-test.ps1 from " + start);
	}

	private static string? FindExecutableOnPath(string fileName) {
		string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (string dir in path.Split(Path.PathSeparator)) {
			if (string.IsNullOrWhiteSpace(dir)) continue;
			try {
				string candidate = Path.Combine(dir, fileName);
				if (File.Exists(candidate)) {
					return candidate;
				}
			} catch (ArgumentException) {
				// malformed PATH entry; skip
			}
		}
		return null;
	}

	private static void TryDelete(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (Exception ex) {
			Logger.Warn(ex, "failed to remove temp dir {0}", path);
		}
	}

	private static void ConfigureNLog() {
		if (LogManager.Configuration is not null) return;
		var config = new LoggingConfiguration();
		var console = new ConsoleTarget("logconsole");
		config.AddRule(LogLevel.Info, LogLevel.Fatal, console);
		LogManager.Configuration = config;
	}
}
