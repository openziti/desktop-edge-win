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
    private const int RouterPort = 13022;
    private const string AdminUser = "admin";
    private const string AdminPassword = "admin";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromSeconds(1);

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);

    public string Home { get; }
    public string RootCaPath => Path.Combine(Home, "pki", "root-ca", "certs", "root-ca.cert");
    // Add Identity by URL rejects a host without a dot, so it cannot take localhost.
    public static string UiControllerUrl => $"https://127.0.0.1:{CtrlPort}";
    // Quickstart rewrites its own ziti-cli.json while running, and a login writing it mid-read makes quickstart exit.
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
        if (Directory.Exists(ThirdPartyPkiRoot)) Directory.Delete(ThirdPartyPkiRoot, true);
        LoggedProcess process = LoggedProcess.Start(zitiBin, QuickstartArgs(home), QuickstartEnvironment(home),
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

    // Quickstart has no switch to turn OIDC off, so its generated config is edited before the first start.
    public static async Task<Quickstart> StartWithoutOidcAsync(string zitiBin, string home)
    {
        Directory.CreateDirectory(home);
        List<string> args = new List<string>(QuickstartArgs(home)) { "--configure-and-exit" };
        await using (LoggedProcess configure = LoggedProcess.Start(zitiBin, args, QuickstartEnvironment(home),
                         Path.Combine(home, "configure.log")))
        {
            int exitCode = await configure.WaitForExitAsync(ReadyTimeout);
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"ziti {string.Join(" ", args)} exited {exitCode}. Log {configure.LogPath}:\n{configure.ReadLog()}");
        }

        string[] configs = await StripOidcAsync(home);
        return await StartAdvertisingOidcAsync(zitiBin, home, configs, false);
    }

    private static string[] QuickstartArgs(string home) => new[]
    {
        "edge", "quickstart", $"--home={home}",
        "--ctrl-address=localhost", $"--ctrl-port={CtrlPort}",
        "--router-address=localhost", $"--router-port={RouterPort}",
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
        CliResult result = RunChecked("edge", "list", "identities", $"name=\"{name}\"", "-j");
        JArray identities = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
        if (identities.Count != 1)
            throw new InvalidOperationException(
                $"expected exactly one identity named {name}, found {identities.Count}. stdout: {result.Stdout}");
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

    public void CreateAuthPolicyForExtJwt(string name, IReadOnlyList<string> signerIds)
    {
        List<string> args = new() { "edge", "create", "auth-policy", name, "--primary-ext-jwt-allowed" };
        foreach (string id in signerIds) args.AddRange(new[] { "--primary-ext-jwt-allowed-signers", id });
        RunChecked(args.ToArray());
    }

    public void DeleteAuthPolicy(string name) => RunChecked("edge", "delete", "auth-policy", name);

    public void SetAuthPolicySecondaryExtJwtSigner(string policy, string signerId) =>
        RunChecked("edge", "update", "auth-policy", policy, "--secondary-req-ext-jwt-signer", signerId);

    public void CreateIdentityWithExternalId(string name, string externalId, string authPolicy) =>
        RunChecked("edge", "create", "identity", name, "--external-id", externalId, "-P", authPolicy);

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

        CliResult list = RunChecked("edge", "list", "cas", $"name=\"{name}\"", "-j");
        JArray cas = JObject.Parse(list.Stdout)["data"] as JArray ?? new JArray();
        if (cas.Count != 1)
            throw new InvalidOperationException($"expected exactly one ca named {name}, found {cas.Count}. stdout: {list.Stdout}");
        string? token = (string?)cas[0]["verificationToken"];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException($"ca {name} has no verificationToken. stdout: {list.Stdout}");

        ClientCert verify = CreateClientCert(name, token, Array.Empty<string>());
        RunChecked("edge", "verify", "ca", name, "--cert", verify.CertPath);
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

    public string CreateOttCaEnrollment(string identityName, string caName)
    {
        string enrollmentId = RunChecked("edge", "create", "enrollment", "ottca", identityName, caName).Stdout.Trim();
        CliResult list = RunChecked("edge", "list", "enrollments", $"id=\"{enrollmentId}\"", "-j");
        JArray enrollments = JObject.Parse(list.Stdout)["data"] as JArray ?? new JArray();
        if (enrollments.Count != 1)
            throw new InvalidOperationException(
                $"expected exactly one enrollment {enrollmentId}, found {enrollments.Count}. stdout: {list.Stdout}");
        string? jwt = (string?)enrollments[0]["jwt"];
        if (string.IsNullOrEmpty(jwt))
            throw new InvalidOperationException($"enrollment {enrollmentId} has no jwt. stdout: {list.Stdout}");
        return jwt;
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
        using HttpResponseMessage auth = await http.PostAsync("/edge/management/v1/authenticate?method=password",
            new StringContent(credentials.ToString(), System.Text.Encoding.UTF8, "application/json"));
        string authBody = await auth.Content.ReadAsStringAsync();
        if (!auth.IsSuccessStatusCode)
            throw new InvalidOperationException($"admin authenticate returned {(int)auth.StatusCode}: {authBody}");
        return (string?)JObject.Parse(authBody)["data"]?["token"]
            ?? throw new InvalidOperationException($"admin authenticate returned no data.token: {authBody}");
    }

    // Its presence sends clients down the OIDC auth path instead of legacy api sessions.
    private const string OidcAuthCapability = "OIDC_AUTH";
    private const string WithOidcSuffix = ".with-oidc";

    public async Task<Quickstart> RestartWithoutOidcAsync()
    {
        // Router enrollment writes the key and cert its config points at, so a restart before then fails.
        await WaitForRouterOnlineAsync();
        await DisposeAsync();
        string[] configs = await StripOidcAsync(Home);
        return await StartAdvertisingOidcAsync(_zitiBin, Home, configs, false);
    }

    public async Task<Quickstart> RestartWithOidcAsync()
    {
        await DisposeAsync();
        string[] saved = Directory.GetFiles(Home, "ctrl.yaml" + WithOidcSuffix, SearchOption.AllDirectories);
        if (saved.Length == 0)
            throw new FileNotFoundException($"no ctrl.yaml{WithOidcSuffix} under {Home}, so OIDC was never stripped");
        foreach (string copy in saved) File.Copy(copy, copy[..^WithOidcSuffix.Length], true);
        return await StartAdvertisingOidcAsync(_zitiBin, Home, saved, true);
    }

    private static async Task<string[]> StripOidcAsync(string home)
    {
        string[] configs = Directory.GetFiles(home, "ctrl.yaml", SearchOption.AllDirectories);
        if (configs.Length == 0) throw new FileNotFoundException($"no ctrl.yaml under {home}");
        foreach (string config in configs)
        {
            File.Copy(config, config + WithOidcSuffix, true);
            await File.WriteAllLinesAsync(config, WithoutOidc(await File.ReadAllLinesAsync(config), config));
        }
        return configs;
    }

    private static async Task<Quickstart> StartAdvertisingOidcAsync(string zitiBin, string home,
        IReadOnlyList<string> configs, bool expectOidc)
    {
        Quickstart quickstart = await StartAsync(zitiBin, home);
        IReadOnlyList<string> capabilities = await quickstart.CapabilitiesAsync();
        if (capabilities.Contains(OidcAuthCapability) != expectOidc)
        {
            await quickstart.DisposeAsync();
            throw new InvalidOperationException(
                $"the controller's {OidcAuthCapability} is {!expectOidc} after editing {string.Join(", ", configs)}, expected {expectOidc}: [{string.Join(", ", capabilities)}]");
        }
        return quickstart;
    }

    // ziti 2.0+ binds OIDC on its own unless edge.api.disableOidcAutoBinding is set.
    private static List<string> WithoutOidc(IReadOnlyList<string> lines, string path)
    {
        List<string> result = new();
        int bindingIndent = -1;
        bool inEdge = false;
        bool autoBindingSet = false;
        bool bindingRemoved = false;
        foreach (string line in lines)
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
            result.Add(line);
            if (line == "edge:") inEdge = true;
            else if (inEdge && line == "  api:")
            {
                result.Add("    disableOidcAutoBinding: true");
                autoBindingSet = true;
                inEdge = false;
            }
        }
        if (!bindingRemoved || !autoBindingSet)
            throw new InvalidDataException(
                $"{path} has no edge-oidc binding ({bindingRemoved}) or no edge.api section ({autoBindingSet})");
        return result;
    }

    private async Task WaitForRouterOnlineAsync()
    {
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        await PollAsync(deadline, "an online router", () => Run("edge", "list", "edge-routers", "-j"), HasOnlineRouter);
    }

    private static bool HasOnlineRouter(CliResult result)
    {
        if (result.ExitCode != 0) return false;
        JArray routers = JObject.Parse(result.Stdout)["data"] as JArray ?? new JArray();
        return routers.OfType<JObject>().Any(r => (bool?)r["isOnline"] == true);
    }

    private async Task<IReadOnlyList<string>> CapabilitiesAsync()
    {
        JObject data = await ClientVersionAsync();
        JArray capabilities = data["capabilities"] as JArray
            ?? throw new InvalidOperationException($"GET {ClientVersionUrl} returned no data.capabilities: {data}");
        return capabilities.Values<string>().Select(c => c!).ToList();
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
        catch (HttpRequestException e) when (e.InnerException is AuthenticationException)
        {
            return false;
        }
    }

    // Until a raft leader is elected the controller rejects every model update with CLUSTER_NO_LEADER.
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
            $"quickstart never reached {waitingFor} in {ReadyTimeout.TotalSeconds}s. Log: {_process.LogPath}");
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

    public async ValueTask DisposeAsync() => await _process.DisposeAsync();
}
