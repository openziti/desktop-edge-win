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
/// Collection fixture. InitializeAsync boots the quickstart and provisions test
/// identities once for the collection; DisposeAsync tears it down.
/// </summary>
public class QuickstartFixture : IAsyncLifetime {
	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	private const string ControllerHost = "localhost";
	private const int ControllerPort = 1280;
	private const string ControllerUrlDefault = "https://localhost:1280";
	private static readonly TimeSpan ControllerStartTimeout = TimeSpan.FromSeconds(60);

	// Only these names are touched by cleanup; other identities on the same ZET are safe.
	private static readonly string[] TestIdentityNames = { "normal-user-01", "normal-user-02", "normal-user-03", "normal-user-04", "normal-user-05", "normal-user-06", "normal-user-07", "normal-user-08", "normal-user-09" };

	private Process? _zitiProcess;
	private string? _quickstartHome;

	public string ZitiHome { get; private set; } = "";
	public string IdentityDir { get; private set; } = "";
	public string ControllerUrl => ControllerUrlDefault;

	public async Task InitializeAsync() {
		ConfigureNLog();
		await RemoveTestIdentitiesViaIpc();
		StartQuickstart();
		CreateTestIdentities();
	}

	public async Task DisposeAsync() {
		try {
			await RemoveTestIdentitiesViaIpc();
		} catch (Exception ex) {
			Logger.Warn(ex, "fixture: IPC cleanup failed");
		}

		try {
			StopZitiProcess();
		} catch (Exception ex) {
			Logger.Warn(ex, "fixture: StopZitiProcess failed");
		}

		if (_quickstartHome is not null) {
			TryDelete(_quickstartHome);
		}
		if (!string.IsNullOrEmpty(ZitiHome)) {
			TryDelete(ZitiHome);
		}
	}

	private static async Task RemoveTestIdentitiesViaIpc() {
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

	private void StartQuickstart() {
		if (_zitiProcess is not null) return;

		bool portAlreadyInUse = TryConnectController();
		if (portAlreadyInUse) {
			throw new InvalidOperationException($"controller port {ControllerPort} is already in use.");
		}

		string zitiExe = FindExecutableOnPath("ziti.exe")
			?? throw new FileNotFoundException("ziti.exe not found on PATH.");

		_quickstartHome = Path.Combine(Path.GetTempPath(), "zdew-quickstart-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_quickstartHome);

		_zitiProcess = LaunchQuickstartProcess(zitiExe, _quickstartHome);
		WaitForController(ControllerStartTimeout);
		Logger.Info("Quickstart started. home={0}", _quickstartHome);
	}

	private void CreateTestIdentities() {
		if (_quickstartHome is null) {
			throw new InvalidOperationException($"{nameof(StartQuickstart)}() must run before {nameof(CreateTestIdentities)}().");
		}

		string zitiExe = FindExecutableOnPath("ziti.exe") ?? throw new FileNotFoundException("ziti.exe not found on PATH.");

		string idHome = Path.Combine(Path.GetTempPath(), "zdew-ids-" + Guid.NewGuid().ToString("N"));
		ZitiHome = idHome;
		IdentityDir = Path.Combine(idHome, "identities");
		Directory.CreateDirectory(IdentityDir);

		RunZiti(zitiExe, "edge", "login", ControllerUrlDefault, "-u", "admin", "-p", "admin", "-y");
		foreach (string name in TestIdentityNames) {
			RunZiti(zitiExe, "edge", "create", "identity", name, "-o", Path.Combine(IdentityDir, name + ".jwt"));
		}

		Logger.Info("Test identities created. IdentityDir={0}", IdentityDir);
	}

	private static void RunZiti(string zitiExe, params string[] args) {
		var psi = new ProcessStartInfo(zitiExe) {
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			Arguments = QuoteArgs(args),
		};

		using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to launch " + zitiExe);

		process.OutputDataReceived += (_, e) => Logger.Debug("[ziti] {0}", e.Data);
		process.ErrorDataReceived += (_, e) => Logger.Warn("[ziti] {0}", e.Data);
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		process.WaitForExit();

		if (process.ExitCode != 0) {
			throw new InvalidOperationException($"ziti {string.Join(" ", args)} exited with code {process.ExitCode}.");
		}
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

	private void WaitForController(TimeSpan timeout) {
		DateTime deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline) {
			if (_zitiProcess is { HasExited: true }) {
				throw new InvalidOperationException($"ziti edge quickstart exited with code {_zitiProcess.ExitCode} before the controller came up.");
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

	private void StopZitiProcess() {
		if (_zitiProcess is null) return;
		try {
			if (!_zitiProcess.HasExited) {
				KillProcessTree(_zitiProcess);
				_zitiProcess.WaitForExit(10_000);
			}
		} catch (Exception ex) {
			Logger.Warn(ex, "failed to stop ziti quickstart");
		} finally {
			_zitiProcess.Dispose();
			_zitiProcess = null;
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

[CollectionDefinition("Quickstart")]
public class QuickstartCollection : ICollectionFixture<QuickstartFixture> { }
