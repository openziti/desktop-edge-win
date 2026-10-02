using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// ZET's test-harness IdP (ziti-tunnel-sdk-c tests/integration/testutil/idp.go): dex serving testdata/dex-config.yaml,
/// a byte-for-byte copy of ZET's, with one static user per ext-auth test.
/// </summary>
public sealed class Dex : IAsyncDisposable
{
    // Must match issuer and web.http in dex-config.yaml.
    public const string IssuerUrl = "http://127.0.0.1:5556/dex";
    // The values ZET's run-ci.ps1 writes into its idp config block.
    public const string ClientIdWorks = "ziti-test";
    public const string ClientIdExtraA = "ziti-test-2";
    public const string ClientIdExtraB = "ziti-test-3";
    public const string Audience = "ziti-test";
    public static readonly IReadOnlyList<string> Scopes = new[] { "openid", "profile", "email", "groups" };
    // The bcrypt hash every user in dex-config.yaml shares.
    private const string Password = "password";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
    private const int MaxHops = 8;
    // ZET's loopback can take a moment to bind after it returns the auth URL.
    private static readonly TimeSpan LoopbackTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex FormRe = new(@"<form([^>]*)>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex InputRe = new(@"<input\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex AttrRe = new(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private sealed record LoginForm(string Action, IReadOnlyDictionary<string, string> Fields);

    public string JwksUri { get; }
    private readonly LoggedProcess _process;

    private Dex(LoggedProcess process, string jwksUri)
    {
        _process = process;
        JwksUri = jwksUri;
    }

    public static async Task<Dex> StartAsync(string dexBin, string configPath, string home)
    {
        LoggedProcess process = LoggedProcess.Start(dexBin, new[] { "serve", configPath },
            new Dictionary<string, string>(), Path.Combine(home, "logs", "dex.log"));
        try
        {
            string jwksUri = await WaitForDiscoveryAsync(process);
            return new Dex(process, jwksUri);
        }
        catch
        {
            await process.DisposeAsync();
            throw;
        }
    }

    /// <summary>The jwks_uri from dex's OIDC discovery document, once it answers.</summary>
    private static async Task<string> WaitForDiscoveryAsync(LoggedProcess process)
    {
        string discovery = $"{IssuerUrl}/.well-known/openid-configuration";
        using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        DateTime deadline = DateTime.UtcNow + ReadyTimeout;
        string lastError = "no attempt";
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException($"dex exited before discovery answered. Log {process.LogPath}:\n{process.ReadLog()}");
            try
            {
                using HttpResponseMessage resp = await client.GetAsync(discovery);
                string body = await resp.Content.ReadAsStringAsync();
                if (resp.StatusCode == HttpStatusCode.OK)
                {
                    string? jwks = (string?)JObject.Parse(body)["jwks_uri"];
                    if (string.IsNullOrEmpty(jwks)) throw new InvalidOperationException($"discovery {discovery} has no jwks_uri: {body}");
                    return jwks;
                }
                lastError = $"status {(int)resp.StatusCode}: {body}";
            }
            catch (HttpRequestException ex)
            {
                lastError = ex.Message;
            }
            catch (TaskCanceledException ex)
            {
                lastError = ex.Message;
            }
            await Task.Delay(150);
        }
        throw new TimeoutException($"dex discovery at {discovery} never answered in {ReadyTimeout.TotalSeconds}s, last: {lastError}. Log: {process.LogPath}");
    }

    /// <summary>
    /// ZET's DriveIdPFlow: do what the browser would, from authUrl through dex's login form to ZET's loopback callback.
    /// ZET gets the auth code and does the PKCE exchange itself.
    /// </summary>
    public static async Task DriveIdPFlowAsync(string authUrl, string email)
    {
        using HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
        using HttpClient client = new HttpClient(handler) { Timeout = HttpTimeout };

        Uri loginPage = await FollowRedirectsTo200Async(client, new Uri(authUrl));
        using HttpResponseMessage page = await client.GetAsync(loginPage);
        string body = await page.Content.ReadAsStringAsync();
        if (page.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"login page {loginPage} status {(int)page.StatusCode}: {Truncate(body)}");

        LoginForm form = ParseLoginForm(body, email);
        Uri postUrl = new Uri(loginPage, form.Action);
        using HttpResponseMessage posted = await client.PostAsync(postUrl, new FormUrlEncodedContent(form.Fields));
        string postBody = await posted.Content.ReadAsStringAsync();
        if (posted.StatusCode != HttpStatusCode.SeeOther && posted.StatusCode != HttpStatusCode.Found)
            throw new InvalidOperationException($"login POST to {postUrl} status {(int)posted.StatusCode}: {Truncate(postBody)}");
        Uri current = new Uri(postUrl, posted.Headers.Location
            ?? throw new InvalidOperationException($"login POST to {postUrl} returned no Location"));

        for (int i = 0; i < MaxHops; i++)
        {
            if (IsLoopbackCallback(current))
            {
                await HitLoopbackAsync(client, current);
                Step.Log($"logged in to dex as {email} and sent the code to {current.GetLeftPart(UriPartial.Path)}");
                return;
            }
            using HttpResponseMessage hop = await client.GetAsync(current);
            if (hop.StatusCode != HttpStatusCode.SeeOther && hop.StatusCode != HttpStatusCode.Found)
                throw new InvalidOperationException($"expected a redirect at {current}, got status {(int)hop.StatusCode}");
            current = new Uri(current, hop.Headers.Location
                ?? throw new InvalidOperationException($"no Location at {current} (status {(int)hop.StatusCode})"));
        }
        throw new InvalidOperationException($"the IdP redirect chain did not reach the loopback callback in {MaxHops} hops, last {current}");
    }

    private static async Task<Uri> FollowRedirectsTo200Async(HttpClient client, Uri start)
    {
        Uri current = start;
        for (int i = 0; i < MaxHops; i++)
        {
            using HttpResponseMessage resp = await client.GetAsync(current);
            int status = (int)resp.StatusCode;
            if (status < 300 || status >= 400) return current;
            current = new Uri(current, resp.Headers.Location
                ?? throw new InvalidOperationException($"redirect with no Location at {current} (status {status})"));
        }
        throw new InvalidOperationException($"too many redirects starting at {start}");
    }

    /// <summary>
    /// ZET's parseLoginForm: the first form with a password input, every input it declares, with the password field and
    /// the first text or email field filled in.
    /// </summary>
    private static LoginForm ParseLoginForm(string body, string email)
    {
        foreach (Match f in FormRe.Matches(body))
        {
            Dictionary<string, string> formAttrs = ParseTagAttributes(f.Groups[1].Value);
            Dictionary<string, string> fields = new();
            string? passField = null;
            string? userField = null;
            foreach (Match input in InputRe.Matches(f.Groups[2].Value))
            {
                Dictionary<string, string> a = ParseTagAttributes(input.Groups[1].Value);
                if (!a.TryGetValue("name", out string? name) || name == "") continue;
                // Hidden state is HTML-escaped, and posting it escaped makes dex answer 400.
                fields[name] = WebUtility.HtmlDecode(a.GetValueOrDefault("value", ""));
                switch (a.GetValueOrDefault("type", "").ToLowerInvariant())
                {
                    case "password":
                        passField = name;
                        break;
                    case "" or "text" or "email" or "tel":
                        userField ??= name;
                        break;
                }
            }
            if (passField == null) continue;
            fields[passField] = Password;
            if (userField != null) fields[userField] = email;
            return new LoginForm(WebUtility.HtmlDecode(formAttrs.GetValueOrDefault("action", "")), fields);
        }
        throw new InvalidOperationException($"no login form with a password field found in: {Truncate(body)}");
    }

    private static Dictionary<string, string> ParseTagAttributes(string tag)
    {
        Dictionary<string, string> attrs = new();
        foreach (Match m in AttrRe.Matches(tag))
            attrs[m.Groups[1].Value.ToLowerInvariant()] = m.Groups[2].Success && m.Groups[2].Value != "" ? m.Groups[2].Value : m.Groups[3].Value;
        return attrs;
    }

    private static bool IsLoopbackCallback(Uri u) =>
        u.AbsolutePath == "/auth/callback" && (u.Host == "localhost" || u.Host == "127.0.0.1" || u.Host == "[::1]");

    private static async Task HitLoopbackAsync(HttpClient client, Uri loopback)
    {
        DateTime deadline = DateTime.UtcNow + LoopbackTimeout;
        while (true)
        {
            try
            {
                using HttpResponseMessage resp = await client.GetAsync(loopback);
                return;
            }
            catch (HttpRequestException ex)
            {
                if (DateTime.UtcNow > deadline) throw new InvalidOperationException($"hit loopback {loopback}: {ex.Message}", ex);
            }
            await Task.Delay(100);
        }
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200] + "...";

    public ValueTask DisposeAsync() => _process.DisposeAsync();
}
