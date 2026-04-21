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
using System.Net.Sockets;
using NLog;
using NLog.Config;
using NLog.Targets;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge.Client.IntegrationTests;

/// <summary>
/// Test classes that need a running controller and test identities call:
///   QuickstartFixture.StartQuickstart();
///   QuickstartFixture.CreateTestIdentities();
/// from their [ClassInitialize]. Both are idempotent. TearDown stops the
/// quickstart process and cleans the temp ZitiHome at assembly cleanup.
/// </summary>
[TestClass]
public static class QuickstartFixture {
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private const string ControllerHost = "localhost";
	private const int ControllerPort = 1280;
	private const string ControllerUrlDefault = "https://localhost:1280";
	private const string RouterNameDefault = "router-quickstart";
	private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan ControllerStartTimeout = TimeSpan.FromSeconds(60);

	// Only these names are touched by cleanup; other identities on the same ZET are safe.
	private static readonly string[] TestIdentityNames = { "normal-user-01", "normal-user-02", "normal-user-03", "normal-user-04" };

	private static Process? zitiProcess;
	private static string? quickstartHome;

	public static string? ZitiHome { get; private set; }
	public static string? IdentityDir { get; private set; }
	public static string ControllerUrl => ControllerUrlDefault;

	[AssemblyInitialize]
	public static void Init(TestContext context) {
		ConfigureNLog();
		RemoveTestIdentitiesViaIpc().GetAwaiter().GetResult();
	}

	[AssemblyCleanup]
	public static void TearDown() {
		RemoveTestIdentitiesViaIpc().GetAwaiter().GetResult();
		StopZitiProcess();

		if (quickstartHome is not null) {
			TryDelete(quickstartHome);
		}
		if (ZitiHome is not null) {
			TryDelete(ZitiHome);
		}
	}

	/// <summary>
	/// Removes loaded test identities via IPC. Called at init and teardown so a crashed
	/// prior run self-heals. Throws if ZET isn't reachable.
	/// </summary>
	public static async Task RemoveTestIdentitiesViaIpc() {
		var client = new DataClient("fixture-cleanup");
		await client.ConnectAsync();
		await client.WaitForConnectionAsync();

		ZitiTunnelStatus status = await client.GetStatusAsync();
		IEnumerable<Identity> loaded = status?.Data?.Identities?
			.Where(i => TestIdentityNames.Contains(i.Name) && !string.IsNullOrEmpty(i.Identifier))
			?? Enumerable.Empty<Identity>();

		foreach (Identity id in loaded) {
			await client.RemoveIdentityAsync(id.Identifier);
			Logger.Info("fixture: removed {0} via IPC", id.Name);
		}
	}

	/// <summary>
	/// Starts a fresh `ziti edge quickstart` in a dedicated temp --home and waits
	/// for the controller to accept TCP connections. Idempotent.
	/// </summary>
	public static void StartQuickstart() {
		if (zitiProcess is not null) return;

		string zitiExe = FindExecutableOnPath("ziti.exe")
			?? throw new FileNotFoundException("ziti.exe not found on PATH.");

		quickstartHome = Path.Combine(Path.GetTempPath(), "zdew-quickstart-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(quickstartHome);

		zitiProcess = LaunchQuickstartProcess(zitiExe, quickstartHome);
		WaitForController(ControllerStartTimeout);
		Logger.Info("Quickstart started. home={0}", quickstartHome);
	}

	/// <summary>
	/// Provisions the standard test identities via scripts/setup-ids-for-test.ps1.
	/// Requires <see cref="StartQuickstart"/> first. Uses a separate temp -ZitiHome
	/// so the script's cleanup can't touch the running quickstart's data.
	/// </summary>
	public static void CreateTestIdentities() {
		if (quickstartHome is null) {
			throw new InvalidOperationException(
				$"Call {nameof(StartQuickstart)}() before {nameof(CreateTestIdentities)}().");
		}

		string pwsh = FindExecutableOnPath("pwsh.exe")
			?? throw new FileNotFoundException("pwsh.exe (PowerShell 7+) not found on PATH.");
		string scriptPath = LocateSetupScript();

		string idHome = Path.Combine(Path.GetTempPath(), "zdew-ids-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(idHome);
		ZitiHome = idHome;
		IdentityDir = Path.Combine(idHome, "identities");

		RunSetupScript(pwsh, scriptPath, idHome);
		Logger.Info("Test identities created. ZitiHome={0}", ZitiHome);
	}

	private static void RunSetupScript(string pwsh, string scriptPath, string tempHome) {
		var args = new[] {
			"-NoProfile",
			"-ExecutionPolicy", "Bypass",
			"-File", scriptPath,
			"-ClearIdentitiesOk",
			"-NonInteractive",
			"-ZitiHome", tempHome,
			"-Url", ControllerUrlDefault,
			"-RouterName", RouterNameDefault,
			"-Normal",
		};

		var psi = new ProcessStartInfo(pwsh) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
			Arguments = QuoteArgs(args),
		};

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException("failed to launch " + pwsh);

		process.OutputDataReceived += (_, e) => Logger.Debug("[setup] {0}", e.Data);
		process.ErrorDataReceived += (_, e) => Logger.Warn("[setup] {0}", e.Data);
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		if (!process.WaitForExit((int)SetupTimeout.TotalMilliseconds)) {
			KillProcessTree(process);
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
		throw new FileNotFoundException("could not locate scripts/setup-ids-for-test.ps1 from " + start);
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

	private static Process LaunchQuickstartProcess(string zitiExe, string zitiHome) {
		var psi = new ProcessStartInfo(zitiExe) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			Arguments = QuoteArgs(new[] { "edge", "quickstart", "--home", zitiHome }),
		};

		Logger.Info("starting: {0} edge quickstart --home {1}", zitiExe, zitiHome);
		var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to launch " + zitiExe);

		process.OutputDataReceived += (_, e) => Logger.Debug("[quickstart] {0}", e.Data);
		process.ErrorDataReceived += (_, e) => Logger.Warn("[quickstart] {0}", e.Data);
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		return process;
	}

	private static void WaitForController(TimeSpan timeout) {
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline) {
			if (zitiProcess is { HasExited: true }) {
				throw new InvalidOperationException($"ziti edge quickstart exited with code {zitiProcess.ExitCode} before the controller came up.");
			}
			if (TryConnectController()) {
				Logger.Info("controller at {0}:{1} is accepting connections", ControllerHost, ControllerPort);
				return;
			}
			Thread.Sleep(500);
		}
		throw new TimeoutException($"controller at {ControllerHost}:{ControllerPort} did not come up within {timeout}.");
	}

	private static bool TryConnectController() {
		try {
			using var tcp = new TcpClient();
			Task connectTask = tcp.ConnectAsync(ControllerHost, ControllerPort);
			return connectTask.Wait(500) && tcp.Connected;
		} catch {
			return false;
		}
	}

	private static void StopZitiProcess() {
		if (zitiProcess is null) return;
		try {
			if (!zitiProcess.HasExited) {
				KillProcessTree(zitiProcess);
				zitiProcess.WaitForExit(10_000);
			}
		} catch (Exception ex) {
			Logger.Warn(ex, "failed to stop ziti quickstart");
		} finally {
			zitiProcess.Dispose();
			zitiProcess = null;
		}
	}

	private static void KillProcessTree(Process process) {
		try {
			var taskkill = Process.Start(new ProcessStartInfo("taskkill") {
				Arguments = $"/F /T /PID {process.Id}",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			});
			taskkill?.WaitForExit(10_000);
		} catch (Exception ex) {
			Logger.Warn(ex, "taskkill failed for PID {0}; falling back to Process.Kill()", process.Id);
			try { process.Kill(); } catch { }
		}
	}

	private static string QuoteArgs(IEnumerable<string> args) {
		var sb = new System.Text.StringBuilder();
		foreach (string arg in args) {
			if (sb.Length > 0) sb.Append(' ');
			if (arg.Length == 0 || arg.IndexOfAny(new[] { ' ', '"', '\t' }) >= 0) {
				sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
			} else {
				sb.Append(arg);
			}
		}
		return sb.ToString();
	}

	// Silent by default. Set INTEGRATION_TEST_LOG=Debug (or Info/Warn/etc.) to enable
	// NLog output. The wrapper script does this automatically when -v is passed.
	private static void ConfigureNLog() {
		if (LogManager.Configuration is not null) return;

		string? level = Environment.GetEnvironmentVariable("INTEGRATION_TEST_LOG");
		if (string.IsNullOrWhiteSpace(level)) return;

		var config = new LoggingConfiguration();
		config.AddRule(LogLevel.FromString(level), LogLevel.Fatal, new ConsoleTarget("logconsole"));
		LogManager.Configuration = config;
	}
}
