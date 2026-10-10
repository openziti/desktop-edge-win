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
    public string RootCaPath => Path.Combine(Home, "pki", "root-ca", "certs", "root-ca.cert");
    // The app's Add Identity by URL rejects a host without a dot, so it cannot take localhost. The controller's server
    // certificate also names 127.0.0.1.
    public static string UiControllerUrl => $"https://127.0.0.1:{CtrlPort}";
    // Apart from quickstart's own config dir: quickstart rewrites its ziti-cli.json while it runs, and a CLI login
    // writing the same file mid-read makes quickstart exit on "unexpected end of JSON input".
    private string CliConfigDir => Path.Combine(Home, "harness-cli-config");
    private readonly string _zitiBin;
    private readonly string _tempDir;
    private readonly LoggedProcess _process;

    private Quickstart(string zitiBin, string home, string tempDir, LoggedProcess process)
    {
        _zitiBin = zitiBin;
        Home = home;
        _tempDir = tempDir;
        _process = process;
    }

    public static async Task<Quickstart> StartAsync(string zitiBin, string home)
    {
        Directory.CreateDirectory(home);
        // ziti v2.0.6's `agent cluster init --pid` reads every gops-agent socket in the temp dir with no deadline, so
        // one left by an earlier quickstart can hang it. Its own temp dir leaves it only its own socket. Not under
        // home: the socket path must fit AF_UNIX's 108 chars, and a checkout's TestResults path can be too deep.
        string tempDir = Directory.CreateTempSubdirectory("zdew-qs-").FullName;
        if (Directory.Exists(ThirdPartyPkiRoot)) Directory.Delete(ThirdPartyPkiRoot, true);
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
                ["TMP"] = tempDir,
            },
            Path.Combine(home, "quickstart.log"));

        Quickstart quickstart = new Quickstart(zitiBin, home, tempDir, process);
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
        JObject identity = ListSingleIdentity(name);
        string? jwt = (string?)identity["enrollment"]?["ott"]?["jwt"];
        if (string.IsNullOrEmpty(jwt))
            throw new InvalidOperationException($"identity {name} has no pending OTT enrollment JWT: {identity}");
        return jwt;
    }

    /// <summary>Whether the controller considers the identity enrolled in TOTP.</summary>
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

    /// <summary>Remove the identity's TOTP enrollment as an admin, through the management API, since no CLI verb does.</summary>
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

    /// <summary>An ext-jwt signer's settings. Every signer gets claims property email and dex's scopes.</summary>
    public sealed record ExtJwtSigner(string Name, string Issuer, string Jwks, string ClientId);

    /// <summary>The enrollment settings UpdateExtJwtSigner writes, all of them on every update.</summary>
    public sealed record SignerEnrollment(bool ToCert, bool ToToken, string NameSelector, string AttrSelector,
        string AuthPolicy);

    public static readonly SignerEnrollment EnrollToNone = new(false, false, "/sub", "", "default");

    /// <summary>Returns the new signer's id.</summary>
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

    /// <summary>An auth policy whose primary auth is ext-jwt through these signers.</summary>
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

    // The same on every machine, because the CA dialog's captured cert and key boxes show these paths. StartAsync
    // empties it, since `ziti pki` refuses to overwrite a CA.
    private static readonly string ThirdPartyPkiRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "zdew-ui-test", "third-party-pki");

    /// <summary>The cert and key files `ziti pki create client` writes.</summary>
    public sealed record ClientCert(string CertPath, string KeyPath);

    /// <summary>A root CA on disk that the controller does not know.</summary>
    public void CreateLocalPkiCa(string name) =>
        RunChecked("pki", "create", "ca", "--pki-root", ThirdPartyPkiRoot, "--ca-file", name, "--ca-name", name);

    /// <summary>
    /// A local CA registered for auth, ottca and autoca, then verified with a cert whose CN is
    /// its verification token. Returns the CA's id.
    /// </summary>
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

    /// <summary>A pending ottca enrollment through caName for an existing identity. Returns its JWT.</summary>
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

    /// <summary>
    /// The CA's enrollment JWT from the management API, since no ziti CLI verb prints it.
    /// </summary>
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

    /// <summary>A password session of its own, rather than the CLI's cached one.</summary>
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
    // Beside each ctrl.yaml, the config as it was before RestartWithoutOidcAsync stripped it.
    private const string WithOidcSuffix = ".with-oidc";

    /// <summary>
    /// Stop this controller, strip OIDC from its config so clients take the legacy auth path, and start it again in the
    /// same home. Quickstart has no switch for it. Run after the fixture import, which needs the OIDC endpoints.
    /// </summary>
    public async Task<Quickstart> RestartWithoutOidcAsync()
    {
        // Router enrollment writes the key and cert its config points at, so a restart before then fails.
        await WaitForRouterOnlineAsync();
        await DisposeAsync();
        string[] configs = Directory.GetFiles(Home, "ctrl.yaml", SearchOption.AllDirectories);
        if (configs.Length == 0) throw new FileNotFoundException($"no ctrl.yaml under {Home}");
        foreach (string config in configs)
        {
            File.Copy(config, config + WithOidcSuffix, true);
            await File.WriteAllLinesAsync(config, WithoutOidc(await File.ReadAllLinesAsync(config), config));
        }

        Quickstart restarted = await StartAsync(_zitiBin, Home);
        IReadOnlyList<string> capabilities = await restarted.CapabilitiesAsync();
        if (capabilities.Contains(OidcAuthCapability))
        {
            await restarted.DisposeAsync();
            throw new InvalidOperationException(
                $"the controller still advertises {OidcAuthCapability} after OIDC was stripped from {string.Join(", ", configs)}: [{string.Join(", ", capabilities)}]");
        }
        return restarted;
    }

    /// <summary>Stop this controller, put back the configs RestartWithoutOidcAsync stripped, and start it again.</summary>
    public async Task<Quickstart> RestartWithOidcAsync()
    {
        await DisposeAsync();
        string[] saved = Directory.GetFiles(Home, "ctrl.yaml" + WithOidcSuffix, SearchOption.AllDirectories);
        if (saved.Length == 0)
            throw new FileNotFoundException($"no ctrl.yaml{WithOidcSuffix} under {Home}, so OIDC was never stripped");
        foreach (string copy in saved) File.Copy(copy, copy[..^WithOidcSuffix.Length], true);

        Quickstart restarted = await StartAsync(_zitiBin, Home);
        IReadOnlyList<string> capabilities = await restarted.CapabilitiesAsync();
        if (!capabilities.Contains(OidcAuthCapability))
        {
            await restarted.DisposeAsync();
            throw new InvalidOperationException(
                $"the controller does not advertise {OidcAuthCapability} after restoring {string.Join(", ", saved)}: [{string.Join(", ", capabilities)}]");
        }
        return restarted;
    }

    /// <summary>
    /// The config lines without the edge-oidc web binding and its indented options and comments, and with
    /// edge.api.disableOidcAutoBinding set, since ziti 2.0+ binds OIDC on its own without that key.
    /// </summary>
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

    /// <summary>The capabilities the client API advertises, which decide the auth path clients take.</summary>
    private async Task<IReadOnlyList<string>> CapabilitiesAsync()
    {
        // Only reads our own localhost controller, whose root CA is not trusted yet.
        using HttpClientHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using HttpClient http = new HttpClient(handler) { Timeout = CommandTimeout };
        string url = $"https://localhost:{CtrlPort}/edge/client/v1/version";
        using HttpResponseMessage response = await http.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET {url} returned {(int)response.StatusCode}: {body}");
        JArray capabilities = JObject.Parse(body)["data"]?["capabilities"] as JArray
            ?? throw new InvalidOperationException($"GET {url} returned no data.capabilities: {body}");
        return capabilities.Values<string>().Select(c => c!).ToList();
    }

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

    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync();
        Directory.Delete(_tempDir, true);
    }
}
