using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Drivers;

// The CLI logs in through a config dir under Home, so the developer's own CLI login is never read or replaced.
public sealed class Quickstart : IAsyncDisposable
{
    // Off the quickstart defaults of 1280 and 3022, so a developer's own quickstart can keep running.
    private const int CtrlPort = 11280;
    private const string AdminUser = "admin";
    private const string AdminPassword = "admin";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    public string Home { get; }
    public string RootCaPath => Path.Combine(Home, "pki", "root-ca", "certs", "root-ca.cert");
    // Add Identity by URL rejects a host without a dot, so it cannot take localhost.
    public static string UiControllerUrl => $"https://127.0.0.1:{CtrlPort}";
    // Quickstart rewrites its own ziti-cli.json while running, and a login writing it mid-read makes quickstart exit.
    private string CliConfigDir => Path.Combine(Home, "harness-cli-config");
    private readonly string _zitiExePath;
    private readonly LoggedProcess _process;

    private Quickstart(string zitiExePath, string home, LoggedProcess process)
    {
        _zitiExePath = zitiExePath;
        Home = home;
        _process = process;
    }

    public static async Task<Quickstart> StartAsync(string zitiExePath, string home)
    {
        Directory.CreateDirectory(home);
        if (Directory.Exists(ThirdPartyPkiRoot)) Directory.Delete(ThirdPartyPkiRoot, true);
        LoggedProcess process = LoggedProcess.Start(zitiExePath, QuickstartArgs(home), QuickstartEnvironment(home),
            Path.Combine(home, "quickstart.log"));

        Quickstart quickstart = new Quickstart(zitiExePath, home, process);
        try
        {
            DateTime deadline = DateTime.UtcNow + ReadyTimeout;
            await quickstart.PollAsync(deadline, "admin login",
                () => quickstart.Run("edge", "login", $"https://localhost:{CtrlPort}", "-u", AdminUser, "-p", AdminPassword,
                    "--yes"),
                result => result.ExitCode == 0);
            // Until a raft leader is elected the controller rejects every model update with CLUSTER_NO_LEADER.
            await quickstart.PollAsync(deadline, "a cluster leader", () => quickstart.Run("ops", "cluster", "list", "-j"),
                result =>
                {
                    if (result.ExitCode != 0) return false;
                    JArray members = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
                    return members.OfType<JObject>().Any(member => (bool?)member["leader"] == true);
                });
        }
        catch
        {
            await quickstart.DisposeAsync();
            throw;
        }
        return quickstart;
    }

    // Quickstart has no switch to turn OIDC off, so its generated config is edited before the first start.
    public static async Task<Quickstart> StartWithoutOidcAsync(string zitiExePath, string home)
    {
        Directory.CreateDirectory(home);
        List<string> args = new List<string>(QuickstartArgs(home)) { "--configure-and-exit" };
        await using (LoggedProcess configure = LoggedProcess.Start(zitiExePath, args, QuickstartEnvironment(home),
                         Path.Combine(home, "configure.log")))
        {
            int exitCode = await configure.WaitForExitAsync(ReadyTimeout);
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"ziti {string.Join(" ", args)} exited {exitCode}. Log {configure.LogPath}:\n{configure.ReadLog()}");
        }

        string[] configs = await StripOidcAsync(home);
        return await StartAdvertisingOidcAsync(zitiExePath, home, configs, false);
    }

    private static string[] QuickstartArgs(string home) => new[]
    {
        "edge", "quickstart", $"--home={home}",
        "--ctrl-address=localhost", $"--ctrl-port={CtrlPort}",
        "--router-address=localhost", "--router-port=13022",
    };

    private static Dictionary<string, string> QuickstartEnvironment(string home) => new Dictionary<string, string>
    {
        ["ZITI_CONFIG_DIR"] = Path.Combine(home, "quickstart-cli-config"),
        ["PFXLOG_NO_JSON"] = "true",
    };

    // The importer skips entities that already exist, which never matters on a fresh quickstart.
    public void ImportFixture(string path) =>
        RunChecked("ops", "import", path, "-u", AdminUser, "-p", AdminPassword, "--yes");

    public string GetJwtFromController(string name)
    {
        JObject identity = ListSingleIdentity(name);
        string? jwt = (string?)identity["enrollment"]?["ott"]?["jwt"];
        if (string.IsNullOrEmpty(jwt))
            throw new InvalidOperationException($"identity {name} has no pending OTT enrollment JWT: {identity}");
        return jwt;
    }

    public bool IdentityMfaEnabled(string name)
    {
        JObject identity = ListSingleIdentity(name);
        return (bool?)identity["isMfaEnabled"]
            ?? throw new InvalidOperationException($"identity {name} has no isMfaEnabled: {identity}");
    }

    private JObject ListSingleIdentity(string name)
    {
        CliResult identityListing = RunChecked("edge", "list", "identities", $"name=\"{name}\"", "-j");
        JArray identities = JObject.Parse(identityListing.Stdout)["data"] as JArray ?? new JArray();
        if (identities.Count != 1)
            throw new InvalidOperationException(
                $"expected exactly one identity named {name}, found {identities.Count}. stdout: {identityListing.Stdout}");
        return (JObject)identities[0];
    }

    // Through the management API, since no CLI verb removes an identity's TOTP enrollment.
    public async Task RemoveIdentityMfaAsync(string name)
    {
        string id = (string?)ListSingleIdentity(name)["id"]
            ?? throw new InvalidOperationException($"identity {name} has no id");
        using HttpClient http = ManagementHttpClient();
        string session = await AdminSessionAsync(http);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, $"/edge/management/v1/identities/{id}/mfa");
        request.Headers.Add("zt-session", session);
        using HttpResponseMessage response = await http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"DELETE identities/{id}/mfa for {name} returned {(int)response.StatusCode}: {body}");
    }

    public void DeleteIdentity(string name) => RunChecked("edge", "delete", "identity", name);

    public sealed record ExtJwtSigner(string Name, string Issuer, string Jwks, string ClientId);

    public sealed record SignerEnrollment(bool ToCert, bool ToToken, string NameSelector, string AttrSelector,
        string AuthPolicy);

    public static readonly SignerEnrollment EnrollToNone = new(false, false, "/sub", "", "default");

    public string CreateExtJwtSigner(ExtJwtSigner signer)
    {
        List<string> args = new()
        {
            "edge", "create", "ext-jwt-signer", signer.Name, signer.Issuer,
            "--jwks-endpoint", signer.Jwks, "--audience", Dex.Audience, "--client-id", signer.ClientId,
            "--external-auth-url", signer.Issuer, "--claims-property", "email",
        };
        foreach (string scope in Dex.Scopes) args.AddRange(new[] { "--scopes", scope });
        return RunChecked(args.ToArray()).Stdout.Trim();
    }

    public void UpdateExtJwtSigner(string name, SignerEnrollment enrollment) =>
        RunChecked("edge", "update", "ext-jwt-signer", name,
            "--enroll-name-claims-selector", enrollment.NameSelector,
            "--enroll-attr-claims-selector", enrollment.AttrSelector,
            "--enroll-auth-policy", enrollment.AuthPolicy,
            $"--enroll-to-cert={enrollment.ToCert.ToString().ToLowerInvariant()}",
            $"--enroll-to-token={enrollment.ToToken.ToString().ToLowerInvariant()}");

    public void DeleteExtJwtSigner(string name) => RunChecked("edge", "delete", "ext-jwt-signer", name);

    public void SetIdentityAuthPolicy(string name, string authPolicy) =>
        RunChecked("edge", "update", "identity", name, "-P", authPolicy);

    // Fixed, because captured CA dialogs show these paths. StartAsync empties it: `ziti pki` won't overwrite a CA.
    private static readonly string ThirdPartyPkiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "zdew-ui-test", "third-party-pki");

    public sealed record ClientCert(string CertPath, string KeyPath);

    public void CreateLocalPkiCa(string name) =>
        RunChecked("pki", "create", "ca", "--pki-root", ThirdPartyPkiRoot, "--ca-file", name, "--ca-name", name);

    public string CreateThirdPartyCa(string name, IReadOnlyList<string> createCaFlags)
    {
        CreateLocalPkiCa(name);
        string caCert = Path.Combine(ThirdPartyPkiRoot, name, "certs", $"{name}.cert");
        List<string> args = new() { "edge", "create", "ca", name, caCert, "--auth", "--ottca", "--autoca" };
        args.AddRange(createCaFlags);
        string id = RunChecked(args.ToArray()).Stdout.Trim();

        CliResult caListing = RunChecked("edge", "list", "cas", $"name=\"{name}\"", "-j");
        JArray namedCas = JObject.Parse(caListing.Stdout)["data"] as JArray ?? new JArray();
        if (namedCas.Count != 1)
            throw new InvalidOperationException($"expected exactly one ca named {name}, found {namedCas.Count}. stdout: {caListing.Stdout}");
        string? token = (string?)namedCas[0]["verificationToken"];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"ca {name} has no verificationToken. stdout: {caListing.Stdout}");

        ClientCert verificationCert = CreateClientCert(name, token, Array.Empty<string>());
        RunChecked("edge", "verify", "ca", name, "--cert", verificationCert.CertPath);
        return id;
    }

    public ClientCert CreateClientCert(string caName, string commonName, IReadOnlyList<string> createClientFlags)
    {
        List<string> args = new()
        {
            "pki", "create", "client", "--pki-root", ThirdPartyPkiRoot, "--ca-name", caName,
            "--client-name", commonName, "--client-file", commonName,
        };
        args.AddRange(createClientFlags);
        RunChecked(args.ToArray());
        return new ClientCert(Path.Combine(ThirdPartyPkiRoot, caName, "certs", $"{commonName}.cert"),
            Path.Combine(ThirdPartyPkiRoot, caName, "keys", $"{commonName}.key"));
    }

    // Through the management API, since no ziti CLI verb prints a CA's JWT.
    public async Task<string> GetCaJwtAsync(string caId)
    {
        using HttpClient http = ManagementHttpClient();
        string session = await AdminSessionAsync(http);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/edge/management/v1/cas/{caId}/jwt");
        request.Headers.Add("zt-session", session);
        using HttpResponseMessage response = await http.SendAsync(request);
        string jwt = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET cas/{caId}/jwt returned {(int)response.StatusCode}: {jwt}");
        return jwt.Trim();
    }

    private static HttpClient ManagementHttpClient() =>
        new HttpClient { BaseAddress = new Uri($"https://localhost:{CtrlPort}"), Timeout = CommandTimeout };

    private static async Task<string> AdminSessionAsync(HttpClient http)
    {
        JObject credentials = new JObject { ["username"] = AdminUser, ["password"] = AdminPassword };
        using HttpResponseMessage authResp = await http.PostAsync("/edge/management/v1/authenticate?method=password",
            new StringContent(credentials.ToString(), System.Text.Encoding.UTF8, "application/json"));
        string authBody = await authResp.Content.ReadAsStringAsync();
        if (!authResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"admin authenticate returned {(int)authResp.StatusCode}: {authBody}");
        return (string?)JObject.Parse(authBody)["data"]?["token"]
            ?? throw new InvalidOperationException($"admin authenticate returned no data.token: {authBody}");
    }

    // Its presence sends clients down the OIDC auth path instead of legacy api sessions.
    private const string OidcAuthCapability = "OIDC_AUTH";
    private const string WithOidcSuffix = ".with-oidc";

    public async Task<Quickstart> RestartWithoutOidcAsync()
    {
        // Router enrollment writes the key and cert its config points at, so a restart before then fails.
        await PollAsync(DateTime.UtcNow + ReadyTimeout, "an online router", () => Run("edge", "list", "edge-routers", "-j"),
            result =>
            {
                if (result.ExitCode != 0) return false;
                JArray routers = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
                return routers.OfType<JObject>().Any(router => (bool?)router["isOnline"] == true);
            });
        await DisposeAsync();
        string[] configs = await StripOidcAsync(Home);
        return await StartAdvertisingOidcAsync(_zitiExePath, Home, configs, false);
    }

    public async Task<Quickstart> RestartWithOidcAsync()
    {
        await DisposeAsync();
        string[] saved = Directory.GetFiles(Home, "ctrl.yaml" + WithOidcSuffix, SearchOption.AllDirectories);
        if (saved.Length == 0)
            throw new FileNotFoundException($"no ctrl.yaml{WithOidcSuffix} under {Home}, so OIDC was never stripped");
        foreach (string copy in saved) File.Copy(copy, copy[..^WithOidcSuffix.Length], true);
        return await StartAdvertisingOidcAsync(_zitiExePath, Home, saved, true);
    }

    // ziti 2.0+ binds OIDC on its own unless edge.api.disableOidcAutoBinding is set.
    private static async Task<string[]> StripOidcAsync(string home)
    {
        string[] configs = Directory.GetFiles(home, "ctrl.yaml", SearchOption.AllDirectories);
        if (configs.Length == 0) throw new FileNotFoundException($"no ctrl.yaml under {home}");
        foreach (string config in configs)
        {
            File.Copy(config, config + WithOidcSuffix, true);
            List<string> stripped = new();
            int bindingIndent = -1;
            bool inEdge = false;
            bool autoBindingSet = false;
            bool bindingRemoved = false;
            foreach (string line in await File.ReadAllLinesAsync(config))
            {
                int indent = line.Length - line.TrimStart(' ').Length;
                if (bindingIndent >= 0 && line.Trim().Length > 0 && indent > bindingIndent) continue;
                bindingIndent = -1;
                if (line.Trim() == "- binding: edge-oidc")
                {
                    bindingIndent = indent;
                    bindingRemoved = true;
                    continue;
                }
                stripped.Add(line);
                if (line == "edge:") inEdge = true;
                else if (inEdge && line == "  api:")
                {
                    stripped.Add("    disableOidcAutoBinding: true");
                    autoBindingSet = true;
                    inEdge = false;
                }
            }
            if (!bindingRemoved || !autoBindingSet)
                throw new InvalidDataException(
                    $"{config} has no edge-oidc binding ({bindingRemoved}) or no edge.api section ({autoBindingSet})");
            await File.WriteAllLinesAsync(config, stripped);
        }
        return configs;
    }

    private static async Task<Quickstart> StartAdvertisingOidcAsync(string zitiExePath, string home,
        IReadOnlyList<string> configs, bool expectOidc)
    {
        Quickstart quickstart = await StartAsync(zitiExePath, home);
        JObject data = await ClientVersionAsync();
        JArray capabilityArray = data["capabilities"] as JArray
            ?? throw new InvalidOperationException($"GET {ClientVersionUrl} returned no data.capabilities: {data}");
        List<string> capabilities = capabilityArray.Values<string>().Select(capability => capability!).ToList();
        if (capabilities.Contains(OidcAuthCapability) != expectOidc)
        {
            await quickstart.DisposeAsync();
            throw new InvalidOperationException(
                $"the controller's {OidcAuthCapability} is {!expectOidc} after editing {string.Join(", ", configs)}, expected {expectOidc}: [{string.Join(", ", capabilities)}]");
        }
        return quickstart;
    }

    public async Task<string> ControllerVersionAsync()
    {
        JObject data = await ClientVersionAsync();
        return (string?)data["version"]
            ?? throw new InvalidOperationException($"GET {ClientVersionUrl} returned no data.version: {data}");
    }

    // From ziti 2.1 ztAPI can carry the client API path, or a bare trailing slash after ext-jwt enroll to cert.
    public static string ZtApiPattern => $@"https://(localhost|127\.0\.0\.1):{CtrlPort}(/|/edge/client/v1)?";

    private static string ClientVersionUrl => $"https://localhost:{CtrlPort}/edge/client/v1/version";

    private static async Task<JObject> ClientVersionAsync()
    {
        // Only reads our own localhost controller, whose root CA is not trusted yet.
        using HttpClientHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using HttpClient http = new HttpClient(handler) { Timeout = CommandTimeout };
        using HttpResponseMessage response = await http.GetAsync(ClientVersionUrl);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET {ClientVersionUrl} returned {(int)response.StatusCode}: {body}");
        return JObject.Parse(body)["data"] as JObject
            ?? throw new InvalidOperationException($"GET {ClientVersionUrl} returned no data: {body}");
    }

    // Whether the controller's TLS chain verifies against the OS trust store.
    public static async Task<bool> CaTrustedAsync()
    {
        using HttpClient http = new HttpClient { Timeout = CommandTimeout };
        try
        {
            using HttpResponseMessage response = await http.GetAsync($"https://localhost:{CtrlPort}/version");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            return false;
        }
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
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new TimeoutException(
            $"quickstart never reached {waitingFor} in {ReadyTimeout.TotalSeconds}s. Log: {_process.LogPath}");
    }

    public CliResult RunChecked(params string[] args)
    {
        CliResult result = Run(args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"ziti {string.Join(" ", args)} exited {result.ExitCode}. stdout: {result.Stdout} stderr: {result.Stderr}");
        return result;
    }

    private CliResult Run(params string[] args)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _zitiExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["ZITI_CONFIG_DIR"] = CliConfigDir;

        string command = "ziti " + string.Join(" ", args);
        using Process ziti = Process.Start(startInfo) ?? throw new InvalidOperationException($"{command} did not start");
        Task<string> stdout = ziti.StandardOutput.ReadToEndAsync();
        Task<string> stderr = ziti.StandardError.ReadToEndAsync();
        if (!ziti.WaitForExit(CommandTimeout))
        {
            ziti.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} still running after {CommandTimeout.TotalSeconds}s");
        }
        return new CliResult(ziti.ExitCode, stdout.Result, stderr.Result);
    }

    public async ValueTask DisposeAsync() => await _process.DisposeAsync();
}
