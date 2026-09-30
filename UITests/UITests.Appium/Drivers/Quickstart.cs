using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// A `ziti edge quickstart` controller and router in its own home directory, with the same `ziti` binary logged in
/// through a config dir under that home, so the developer's own CLI login is never read or replaced.
/// </summary>
public sealed class Quickstart : IAsyncDisposable
{
    // Off the quickstart defaults of 1280 and 3022, so a developer's own quickstart can keep running.
    private const int CtrlPort = 11280;
    private const int RouterPort = 13022;
    private const string AdminUser = "admin";
    private const string AdminPassword = "admin";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromSeconds(1);

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    public string Home { get; }
    // Apart from quickstart's own config dir: quickstart rewrites its ziti-cli.json while it runs, and a CLI login
    // writing the same file mid-read makes quickstart exit on "unexpected end of JSON input".
    private string CliConfigDir => Path.Combine(Home, "harness-cli-config");
    private readonly string _zitiBin;
    private readonly LoggedProcess _process;

    private Quickstart(string zitiBin, string home, LoggedProcess process)
    {
        _zitiBin = zitiBin;
        Home = home;
        _process = process;
    }

    public static async Task<Quickstart> StartAsync(string zitiBin, string home)
    {
        Directory.CreateDirectory(home);
        LoggedProcess process = LoggedProcess.Start(zitiBin,
            new[]
            {
                "edge", "quickstart", $"--home={home}",
                "--ctrl-address=localhost", $"--ctrl-port={CtrlPort}",
                "--router-address=localhost", $"--router-port={RouterPort}",
            },
            new Dictionary<string, string>
            {
                ["ZITI_CONFIG_DIR"] = Path.Combine(home, "quickstart-cli-config"),
                ["PFXLOG_NO_JSON"] = "true",
            },
            Path.Combine(home, "quickstart.log"));

        Quickstart quickstart = new Quickstart(zitiBin, home, process);
        try
        {
            await quickstart.WaitUntilReadyAsync();
        }
        catch
        {
            await quickstart.DisposeAsync();
            throw;
        }
        return quickstart;
    }

    /// <summary>
    /// Create the entities declared in a `ziti ops import` file. The importer skips entities that already exist, which
    /// never matters here because every run starts a fresh quickstart.
    /// </summary>
    public void ImportFixture(string path) =>
        RunChecked("ops", "import", path, "-u", AdminUser, "-p", AdminPassword, "--yes");

    /// <summary>The pending one-time-token enrollment JWT of an imported identity that is not enrolled yet.</summary>
    public string GetJwtFromController(string name)
    {
        string filter = $"name=\"{name}\"";
        CliResult result = RunChecked("edge", "list", "identities", filter, "-j");
        JArray identities = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
        if (identities.Count != 1)
            throw new InvalidOperationException(
                $"expected exactly one identity named {name}, found {identities.Count}. stdout: {result.Stdout}");
        string? jwt = (string?)identities[0]["enrollment"]?["ott"]?["jwt"];
        if (string.IsNullOrEmpty(jwt))
            throw new InvalidOperationException($"identity {name} has no pending OTT enrollment JWT. stdout: {result.Stdout}");
        return jwt;
    }

    public void DeleteIdentity(string name) => RunChecked("edge", "delete", "identity", name);

    /// <summary>
    /// Log in once the controller answers, then wait for a raft leader: until one is elected the controller rejects
    /// every model update with CLUSTER_NO_LEADER.
    /// </summary>
    private async Task WaitUntilReadyAsync()
    {
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        await PollAsync(deadline, "admin login",
            () => Run("edge", "login", $"https://localhost:{CtrlPort}", "-u", AdminUser, "-p", AdminPassword, "--yes"),
            r => r.ExitCode == 0);
        await PollAsync(deadline, "a cluster leader", () => Run("ops", "cluster", "list", "-j"), HasLeader);
    }

    private async Task<CliResult> PollAsync(DateTime deadline, string waitingFor, Func<CliResult> attempt,
        Func<CliResult, bool> done)
    {
        CliResult last;
        while (true)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"quickstart exited while waiting for {waitingFor}. Log {_process.LogPath}:\n{_process.ReadLog()}");
            last = attempt();
            if (done(last)) return last;
            if (DateTime.UtcNow > deadline) break;
            await Task.Delay(ReadyPollInterval);
        }
        throw new TimeoutException(
            $"quickstart never reached {waitingFor} in {ReadyTimeout.TotalSeconds}s. Last CLI exit {last.ExitCode}, " +
            $"stdout: {last.Stdout} stderr: {last.Stderr}. Log: {_process.LogPath}");
    }

    private static bool HasLeader(CliResult result)
    {
        if (result.ExitCode != 0) return false;
        JArray members = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
        return members.OfType<JObject>().Any(m => (bool?)m["leader"] == true);
    }

    private CliResult RunChecked(params string[] args)
    {
        CliResult result = Run(args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"ziti {string.Join(" ", args)} exited {result.ExitCode}. stdout: {result.Stdout} stderr: {result.Stderr}");
        return result;
    }

    private CliResult Run(params string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = _zitiBin,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["ZITI_CONFIG_DIR"] = CliConfigDir;

        string command = "ziti " + string.Join(" ", args);
        using Process p = Process.Start(psi) ?? throw new InvalidOperationException($"{command} did not start");
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(CommandTimeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} still running after {CommandTimeout.TotalSeconds}s");
        }
        return new CliResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    public ValueTask DisposeAsync() => _process.DisposeAsync();
}
