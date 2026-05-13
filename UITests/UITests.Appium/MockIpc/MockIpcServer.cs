using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.MockIpc;

public sealed class MockIpcServer : IAsyncDisposable
{
    public string PipePrefix { get; }
    public string DataIpcPipeName => PipePrefix + "ziti-edge-tunnel.sock";
    public string DataEventPipeName => PipePrefix + "ziti-edge-tunnel-event.sock";
    public string MonitorIpcPipeName => PipePrefix + @"OpenZiti\ziti-monitor\ipc";
    public string MonitorEventPipeName => PipePrefix + @"OpenZiti\ziti-monitor\events";

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _serverLoops = new();
    private readonly JObject _landingStatus;
    private readonly object _recvLock = new();
    private readonly List<JObject> _received = new();
    private readonly List<JObject> _receivedMonitor = new();
    private readonly Channel<JObject> _eventPush = Channel.CreateUnbounded<JObject>();

    // Queue of next responses for AddIdentity. Each entry is either a
    // success (with assigned identity name) or a failure (with error message).
    private readonly Queue<AddIdentityNextResponse> _addIdentityQueue = new();
    private readonly object _addIdentityLock = new();

    private record AddIdentityNextResponse(bool Success, string? Name, string? Error);

    // Per-identity TOTP secret (base32) generated on EnableMFA. VerifyMFA
    // validates submitted codes against this secret using real RFC 6238 TOTP.
    private readonly Dictionary<string, string> _mfaSecrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mfaSecretsLock = new();

    /// <summary>
    /// Returns the base32 TOTP secret minted for the given identity by the most
    /// recent EnableMFA call, or null if EnableMFA hasn't run for it yet. Tests
    /// use this to compute the correct TOTP code via Totp.Compute(secret).
    /// </summary>
    public string? GetMfaSecret(string identifier)
    {
        lock (_mfaSecretsLock)
        {
            return _mfaSecrets.TryGetValue(identifier, out var s) ? s : null;
        }
    }

    public IReadOnlyList<JObject> ReceivedRequests
    {
        get { lock (_recvLock) return _received.ToArray(); }
    }

    public IReadOnlyList<string> ReceivedCommandNames
    {
        get { lock (_recvLock) return _received.Select(r => (string?)r["Command"] ?? "").ToArray(); }
    }

    public IReadOnlyList<JObject> ReceivedMonitorRequests
    {
        get { lock (_recvLock) return _receivedMonitor.ToArray(); }
    }

    public IReadOnlyList<string> ReceivedMonitorOps
    {
        get { lock (_recvLock) return _receivedMonitor.Select(r => (string?)r["Op"] ?? "").ToArray(); }
    }

    /// <summary>
    /// Simulate the tunneler reporting a successful external-auth login for the
    /// given identity. Clicking the "Authenticate with Provider" button in real
    /// life launches a browser via Process.Start(url) -- we don't want a real
    /// browser popping up during a test run. Pushing this event reproduces the
    /// post-login state that ZDEW expects to see from ziti-edge-tunnel: an
    /// identity event with Action="added" and NeedsExtAuth=false, which the
    /// MainWindow handler treats as a successful authentication.
    /// </summary>
    /// <summary>
    /// Queue the next AddIdentity IPC response. Each call to this is consumed
    /// by exactly one AddIdentity command from the UI. On success, the mock
    /// appends a fresh identity with the given name to its cached landing
    /// status (so subsequent Status queries / events reflect it) and returns
    /// a populated Identity payload. On failure, returns Success=false with
    /// the supplied error message; the WPF surfaces this as a blurb.
    /// </summary>
    public void EnqueueAddIdentitySuccess(string identityName)
    {
        lock (_addIdentityLock) _addIdentityQueue.Enqueue(new AddIdentityNextResponse(true, identityName, null));
    }

    public void EnqueueAddIdentityFailure(string error = "Mock-controlled AddIdentity failure")
    {
        lock (_addIdentityLock) _addIdentityQueue.Enqueue(new AddIdentityNextResponse(false, null, error));
    }

    public void PushExtAuthSuccess(string identifier)
    {
        var identities = _landingStatus["Identities"] as JArray;
        var id = identities?
            .OfType<JObject>()
            .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase));
        if (id == null) throw new InvalidOperationException($"PushExtAuthSuccess: no identity '{identifier}' in fixture.");

        // Clear the ext-auth requirement on the cached status so any subsequent
        // Status query reflects the post-login state.
        id["NeedsExtAuth"] = false;

        var evt = new JObject
        {
            ["Op"] = "identity",
            ["Action"] = "added",
            ["Fingerprint"] = id["FingerPrint"],
            ["Id"] = id.DeepClone(),
        };
        _eventPush.Writer.TryWrite(evt);
    }

    public MockIpcServer(string pipePrefix, string fixturesDir, string fixtureFile = "landing-status.json")
    {
        PipePrefix = pipePrefix;
        var path = Path.Combine(fixturesDir, fixtureFile);
        _landingStatus = JObject.Parse(File.ReadAllText(path));
    }

    public MockIpcServer(string pipePrefix, JObject landingStatus)
    {
        PipePrefix = pipePrefix;
        _landingStatus = landingStatus;
    }

    public void Start()
    {
        _serverLoops.Add(Task.Run(() => DataIpcLoopAsync(_cts.Token)));
        _serverLoops.Add(Task.Run(() => DataEventLoopAsync(_cts.Token)));
        _serverLoops.Add(Task.Run(() => MonitorIpcLoopAsync(_cts.Token)));
        _serverLoops.Add(Task.Run(() => QuietPipeLoopAsync(MonitorEventPipeName, _cts.Token)));
    }

    private async Task DataIpcLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream srv;
            try
            {
                srv = new NamedPipeServerStream(
                    DataIpcPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (Exception) { return; }

            try { await srv.WaitForConnectionAsync(ct); }
            catch { srv.Dispose(); return; }

            _ = Task.Run(() => HandleDataIpcClientAsync(srv, ct), ct);
        }
    }

    private async Task HandleDataIpcClientAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        using (var reader = new StreamReader(srv, new UTF8Encoding(false), false, 16 * 1024, leaveOpen: true))
        using (var writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            while (srv.IsConnected && !ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync().WaitAsync(ct); }
                catch { return; }
                if (line == null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject reply;
                try
                {
                    var req = JObject.Parse(line);
                    lock (_recvLock) _received.Add(req);
                    reply = BuildReply(req);
                }
                catch (Exception ex)
                {
                    reply = new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = ex.Message };
                }
                await writer.WriteLineAsync(reply.ToString(Formatting.None));
            }
        }
    }

    private JObject BuildReply(JObject req)
    {
        var command = (string?)req["Command"] ?? "";
        var data = req["Data"] as JObject ?? new JObject();

        return command switch
        {
            "Status" => new JObject
            {
                ["Success"] = true,
                ["Code"] = 0,
                ["Data"] = _landingStatus,
            },
            "IdentityOnOff" => HandleIdentityOnOff(data),
            "AddIdentity" => HandleAddIdentity(data),
            "ExternalAuth" => HandleExternalAuth(data),
            "UpdateInterfaceConfig" => HandleUpdateInterfaceConfig(data),
            "EnableMFA" => HandleEnableMFA(data),
            "VerifyMFA" => HandleMfaResult(data, "enrollment_verification"),
            "RemoveMFA" => HandleMfaResult(data, "enrollment_remove"),
            "SubmitMFA" => HandleMfaResult(data, "mfa_auth_status"),
            "GenerateMFACodes" => HandleGenerateMFACodes(data),
            _ => new JObject { ["Success"] = true, ["Code"] = 0 },
        };
    }

    /// <summary>
    /// The 6-digit MFA code the mock accepts. Anything else is rejected; the
    /// magic 666666 is rejected with an explicit "wrong code" failure so tests
    /// can drive both happy- and error-path flows.
    /// </summary>
    public const string AcceptedMfaCode = "123456";
    public const string RejectedMfaCode = "666666";

    private JObject HandleMfaResult(JObject data, string action)
    {
        var identifier = (string?)data["Identifier"] ?? "";
        var code = (string?)data["Code"] ?? "";
        var fingerprint = (_landingStatus["Identities"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))
            ?["FingerPrint"]?.ToString() ?? "MOCKFP";

        // Gate on the submitted code:
        //   * empty code -> enrollment path (no token yet) -> success
        //   * "666666" (RejectedMfaCode) -> canonical failure path for tests
        //     that want a deterministic rejection
        //   * "123456" (AcceptedMfaCode) -> legacy magic-accept for tests that
        //     don't want to drive real TOTP (e.g. disable-MFA flow where the
        //     code isn't generated from a known secret)
        //   * otherwise -> real RFC 6238 TOTP validation against the secret
        //     issued by HandleEnableMFA
        bool successful;
        if (string.IsNullOrEmpty(code))
        {
            successful = true;
        }
        else if (code == RejectedMfaCode)
        {
            successful = false;
        }
        else if (code == AcceptedMfaCode)
        {
            successful = true;
        }
        else
        {
            string? secret;
            lock (_mfaSecretsLock) _mfaSecrets.TryGetValue(identifier, out secret);
            successful = secret != null && Totp.Validate(secret, code);
        }

        var evt = new JObject
        {
            ["Op"] = "mfa",
            ["Action"] = action,
            ["Identifier"] = identifier,
            ["Fingerprint"] = fingerprint,
            ["Successful"] = successful,
        };
        if (!successful)
        {
            evt["Error"] = code == RejectedMfaCode
                ? "MFA code rejected by mock (666666 is the canonical fail code)."
                : $"MFA code '{code}' is not a recognised mock code (try 123456 to succeed or 666666 to fail).";
        }

        // On a successful enrollment_verification, persist MfaEnabled=true on
        // the cached identity so subsequent IdentityOnOff cycles (disable then
        // re-enable) can correctly drive the "identity has MFA, needs re-auth"
        // state.
        if (successful && action == "enrollment_verification")
        {
            var ident = (_landingStatus["Identities"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase));
            if (ident != null) ident["MfaEnabled"] = true;
        }
        // RemoveMFA success clears MfaEnabled.
        if (successful && action == "enrollment_remove")
        {
            var ident = (_landingStatus["Identities"] as JArray)?
                .OfType<JObject>()
                .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase));
            if (ident != null)
            {
                ident["MfaEnabled"] = false;
                ident["MfaNeeded"] = false;
            }
        }

        // CRITICAL ORDERING: push the event AFTER a small delay so the reply for
        // VerifyMFA gets back to the WPF (and DoSetupAuthenticate's OnClose
        // runs) BEFORE the enrollment_verification event arrives. Without this
        // delay, WPF's data and event pipes race -- if the event wins,
        // ShowMFARecoveryCodes opens the recovery screen and then OnClose
        // immediately hides it. Real ziti-edge-tunnel emits the response and
        // event from the same socket in deterministic order; our two-pipe mock
        // can interleave, so we serialise here.
        _ = Task.Run(async () =>
        {
            await Task.Delay(120);
            _eventPush.Writer.TryWrite(evt);
        });

        return new JObject { ["Success"] = successful, ["Code"] = successful ? 0 : 1 };
    }

    private JObject HandleAddIdentity(JObject data)
    {
        AddIdentityNextResponse resp;
        lock (_addIdentityLock)
        {
            resp = _addIdentityQueue.Count > 0
                ? _addIdentityQueue.Dequeue()
                : new AddIdentityNextResponse(true, "mock-default", null);
        }

        if (!resp.Success)
        {
            return new JObject
            {
                ["Success"] = false,
                ["Code"] = 1,
                ["Error"] = resp.Error ?? "Mock-controlled AddIdentity failure",
            };
        }

        // Append a fresh identity to the cached landing status so later Status
        // queries + IdentityOnOff lookups see it. Shape mirrors the JSON
        // fixtures.
        var name = resp.Name ?? "mock-identity";
        var newId = new JObject
        {
            ["Name"] = name,
            ["Identifier"] = $"c:\\fake\\ids\\{name}.json",
            ["FingerPrint"] = "FP" + name.Replace("-", "").ToUpperInvariant(),
            ["Active"] = true,
            ["Loaded"] = true,
            ["Config"] = new JObject
            {
                ["ztAPI"] = "https://controller.example",
                ["ztAPIs"] = new JArray("https://controller.example"),
            },
            ["ControllerVersion"] = "v1.6.15-mock",
            ["IdFileStatus"] = false,
            ["NeedsExtAuth"] = false,
            ["ExtAuthProviders"] = new JArray(),
            ["MfaEnabled"] = false,
            ["MfaNeeded"] = false,
            // Two stock services per dynamically-added identity: one dial-only,
            // one bind-only. Lets tests on the details screen exercise the
            // service list rendering with mixed-permission rows.
            ["Services"] = new JArray
            {
                new JObject
                {
                    ["Id"] = $"svc-dial-{name}",
                    ["Name"] = $"{name}.dial.example",
                    ["Protocols"] = new JArray("tcp"),
                    ["Addresses"] = new JArray(new JObject
                    {
                        ["IsHost"] = true,
                        ["HostName"] = $"{name}.dial.example",
                        ["Prefix"] = 0,
                    }),
                    ["Ports"] = new JArray(new JObject { ["Low"] = 443, ["High"] = 443 }),
                    ["OwnsIntercept"] = true,
                    ["IsAccessible"] = true,
                    ["Timeout"] = -1,
                    ["TimeoutRemaining"] = -1,
                    ["Permissions"] = new JObject { ["Bind"] = false, ["Dial"] = true },
                },
                new JObject
                {
                    ["Id"] = $"svc-bind-{name}",
                    ["Name"] = $"{name}.bind.example",
                    ["Protocols"] = new JArray("tcp"),
                    ["Addresses"] = new JArray(new JObject
                    {
                        ["IsHost"] = true,
                        ["HostName"] = $"{name}.bind.example",
                        ["Prefix"] = 0,
                    }),
                    ["Ports"] = new JArray(new JObject { ["Low"] = 8080, ["High"] = 8080 }),
                    ["OwnsIntercept"] = false,
                    ["IsAccessible"] = true,
                    ["Timeout"] = -1,
                    ["TimeoutRemaining"] = -1,
                    ["Permissions"] = new JObject { ["Bind"] = true, ["Dial"] = false },
                },
            },
            ["Metrics"] = new JObject { ["Up"] = 0, ["Down"] = 0 },
            ["MfaMinTimeout"] = 0,
            ["MfaMaxTimeout"] = 0,
            ["MfaMinTimeoutRem"] = 0,
            ["MfaMaxTimeoutRem"] = 0,
            ["MinTimeoutRemInSvcEvent"] = 0,
            ["MaxTimeoutRemInSvcEvent"] = 0,
            ["Deleted"] = false,
            ["Notified"] = false,
        };

        var identities = (JArray)_landingStatus["Identities"]!;
        identities.Add(newId);

        return new JObject
        {
            ["Success"] = true,
            ["Code"] = 0,
            ["Data"] = newId,
        };
    }

    private JObject HandleGenerateMFACodes(JObject data)
    {
        var identifier = (string?)data["Identifier"] ?? "";
        var code = (string?)data["Code"] ?? "";

        // Same code-gate as HandleMfaResult.
        bool valid;
        if (string.IsNullOrEmpty(code)) valid = false;
        else if (code == RejectedMfaCode) valid = false;
        else if (code == AcceptedMfaCode) valid = true;
        else
        {
            string? secret;
            lock (_mfaSecretsLock) _mfaSecrets.TryGetValue(identifier, out secret);
            valid = secret != null && Totp.Validate(secret, code);
        }

        if (!valid)
        {
            return new JObject
            {
                ["Success"] = false,
                ["Code"] = 1,
                ["Error"] = "Invalid MFA code for regenerate.",
            };
        }

        // Issue 20 freshly-numbered recovery codes so the test can visually
        // confirm the regenerate happened (codes differ from the initial set).
        var newCodes = new JArray();
        var rnd = new Random();
        for (int i = 0; i < 20; i++) newCodes.Add($"REGEN{i:D2}{rnd.Next(100, 999)}");

        return new JObject
        {
            ["Success"] = true,
            ["Code"] = 0,
            ["Data"] = new JObject
            {
                ["Identifier"] = identifier,
                ["RecoveryCodes"] = newCodes,
            },
        };
    }

    private JObject HandleExternalAuth(JObject data)
    {
        var identifier = (string?)data["Identifier"] ?? "";
        var provider = (string?)data["Provider"] ?? "mock-provider";
        var fakeUrl = $"https://idp.example/auth?provider={provider}&state=MOCKSTATE&redirect=http://localhost:54321/auth/callback";
        return new JObject
        {
            ["Success"] = true,
            ["Code"] = 0,
            ["Data"] = new JObject
            {
                ["identifier"] = identifier,
                ["url"] = fakeUrl,
            },
        };
    }

    private JObject HandleUpdateInterfaceConfig(JObject data)
    {
        // Apply the new values to our cached status so the UI keeps a consistent
        // picture if anyone queries Status again.
        var l3 = data["L3"] as JObject;
        var l2 = data["L2"] as JObject;
        if (l3 != null)
        {
            _landingStatus["TunIpv4"] = l3["TunIPv4"];
            _landingStatus["TunIpv4Mask"] = l3["TunPrefixLength"];
            _landingStatus["AddDns"] = l3["AddDns"];
            _landingStatus["ApiPageSize"] = l3["ApiPageSize"];
        }
        if (l2 != null)
        {
            _landingStatus["L2Enabled"] = l2["Enabled"];
            _landingStatus["PcapInterface"] = l2["PcapInterface"];
        }
        return new JObject { ["Success"] = true, ["Code"] = 0 };
    }

    private JObject HandleEnableMFA(JObject data)
    {
        var identifier = (string?)data["Identifier"] ?? "";
        var fingerprint = (_landingStatus["Identities"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase))
            ?["FingerPrint"]?.ToString() ?? "MOCKFP";

        // Generate a REAL RFC 6238 TOTP secret + persist per-identity. The
        // VerifyMFA handler validates submitted codes against this secret;
        // tests use GetMfaSecret(identifier) + Totp.Compute() to produce the
        // matching code.
        var secret = Totp.GenerateSecret();
        lock (_mfaSecretsLock) _mfaSecrets[identifier] = secret;

        var url = $"otpauth://totp/openziti.io:{Uri.EscapeDataString(identifier)}?issuer=openziti.io&secret={secret}";
        var codes = new JArray(
            "AAAAAA", "BBBBBB", "CCCCCC", "DDDDDD", "EEEEEE",
            "FFFFFF", "GGGGGG", "HHHHHH", "IIIIII", "JJJJJJ",
            "KKKKKK", "LLLLLL", "MMMMMM", "NNNNNN", "OOOOOO",
            "PPPPPP", "QQQQQQ", "RRRRRR", "SSSSSS", "TTTTTT");

        // Push the matching enrollment_challenge event so the UI shows the QR dialog.
        var challenge = new JObject
        {
            ["Op"] = "mfa",
            ["Action"] = "enrollment_challenge",
            ["Identifier"] = identifier,
            ["Fingerprint"] = fingerprint,
            ["Successful"] = true,
            ["ProvisioningUrl"] = url,
            ["RecoveryCodes"] = codes,
        };
        _eventPush.Writer.TryWrite(challenge);

        return new JObject
        {
            ["Success"] = true,
            ["Code"] = 0,
            ["Data"] = new JObject
            {
                ["Identifier"] = identifier,
                ["IsVerified"] = false,
                ["ProvisioningUrl"] = url,
                ["RecoveryCodes"] = codes,
            },
        };
    }

    private JObject HandleIdentityOnOff(JObject data)
    {
        var identifier = (string?)data["Identifier"] ?? "";
        var onOff = (bool?)data["OnOff"] ?? false;

        // Locate and mutate the identity in our cached status so the UI keeps
        // a consistent picture if anyone queries Status again.
        var identities = _landingStatus["Identities"] as JArray;
        JObject? id = identities?
            .OfType<JObject>()
            .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase));
        if (id != null)
        {
            id["Active"] = onOff;

            // Re-enabling an identity that has MFA configured triggers a fresh
            // auth challenge: real ziti-edge-tunnel reports MfaNeeded=true on
            // the post-enable status. Disable always clears MfaNeeded.
            if (onOff && (bool?)id["MfaEnabled"] == true)
            {
                id["MfaNeeded"] = true;
            }
            else if (!onOff)
            {
                id["MfaNeeded"] = false;
            }
        }

        // Push the controller event the real ziti-edge-tunnel would emit so the
        // UI flips the ENABLED/DISABLED label and dot colour.
        if (id != null)
        {
            var evt = new JObject
            {
                ["Op"] = "controller",
                ["Action"] = onOff ? "connected" : "disconnected",
                ["Identifier"] = id["Identifier"],
                ["Fingerprint"] = id["FingerPrint"],
            };
            _eventPush.Writer.TryWrite(evt);

            // And an identity/updated push so the toggle state is reflected in the
            // identity model (some UI bindings key on this, not just controller).
            var updated = new JObject
            {
                ["Op"] = "identity",
                ["Action"] = "updated",
                ["Fingerprint"] = id["FingerPrint"],
                ["Id"] = id,
            };
            _eventPush.Writer.TryWrite(updated);
        }

        return new JObject
        {
            ["Success"] = true,
            ["Code"] = 0,
            ["Data"] = new JObject
            {
                ["Command"] = "IdentityOnOff",
                ["Data"] = new JObject
                {
                    ["OnOff"] = onOff,
                    ["Identifier"] = identifier,
                },
            },
        };
    }

    private async Task DataEventLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream srv;
            try
            {
                srv = new NamedPipeServerStream(
                    DataEventPipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch { return; }

            try { await srv.WaitForConnectionAsync(ct); }
            catch { srv.Dispose(); return; }

            _ = Task.Run(() => HandleDataEventClientAsync(srv, ct), ct);
        }
    }

    private async Task HandleDataEventClientAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        using (var writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            var statusPush = new JObject { ["Op"] = "status", ["Status"] = _landingStatus };
            await writer.WriteLineAsync(statusPush.ToString(Formatting.None));

            foreach (var id in _landingStatus["Identities"] as JArray ?? new JArray())
            {
                var added = new JObject
                {
                    ["Op"] = "identity",
                    ["Action"] = "added",
                    ["Fingerprint"] = id["FingerPrint"],
                    ["Id"] = id,
                };
                await writer.WriteLineAsync(added.ToString(Formatting.None));

                if ((bool?)id["NeedsExtAuth"] == true)
                {
                    var ext = new JObject
                    {
                        ["Op"] = "identity",
                        ["Action"] = "needs_ext_login",
                        ["Fingerprint"] = id["FingerPrint"],
                        ["Id"] = id,
                    };
                    await writer.WriteLineAsync(ext.ToString(Formatting.None));
                }

                if (id["Services"] is JArray svcs && svcs.Count > 0)
                {
                    var updated = new JObject
                    {
                        ["Op"] = "identity",
                        ["Action"] = "updated",
                        ["Fingerprint"] = id["FingerPrint"],
                        ["Id"] = id,
                    };
                    await writer.WriteLineAsync(updated.ToString(Formatting.None));
                }

                if ((bool?)id["Active"] == true)
                {
                    var ctrl = new JObject
                    {
                        ["Op"] = "controller",
                        ["Action"] = "connected",
                        ["Identifier"] = id["Identifier"],
                        ["Fingerprint"] = id["FingerPrint"],
                    };
                    await writer.WriteLineAsync(ctrl.ToString(Formatting.None));
                }
            }

            // Drain pushed events for the lifetime of this connection.
            try
            {
                await foreach (var evt in _eventPush.Reader.ReadAllAsync(ct))
                {
                    if (!srv.IsConnected) return;
                    await writer.WriteLineAsync(evt.ToString(Formatting.None));
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    private async Task MonitorIpcLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream srv;
            try
            {
                srv = new NamedPipeServerStream(
                    MonitorIpcPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch { return; }

            try { await srv.WaitForConnectionAsync(ct); }
            catch { srv.Dispose(); return; }

            _ = Task.Run(() => HandleMonitorIpcClientAsync(srv, ct), ct);
        }
    }

    private async Task HandleMonitorIpcClientAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        using (var reader = new StreamReader(srv, new UTF8Encoding(false), false, 16 * 1024, leaveOpen: true))
        using (var writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            while (srv.IsConnected && !ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync().WaitAsync(ct); }
                catch { return; }
                if (line == null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JObject reply;
                try
                {
                    var req = JObject.Parse(line);
                    lock (_recvLock) _receivedMonitor.Add(req);
                    reply = BuildMonitorReply(req);
                }
                catch (Exception ex)
                {
                    reply = new JObject { ["Code"] = 1, ["Message"] = ex.Message };
                }
                await writer.WriteLineAsync(reply.ToString(Formatting.None));
            }
        }
    }

    private JObject BuildMonitorReply(JObject req)
    {
        // The real ZitiUpdateService monitor IPC uses {Op, Action} requests and
        // replies with a SvcResponse-shaped {Code, Message, ...}. For most ops
        // a simple Code:0 success is enough to keep the UI moving.
        var op = (string?)req["Op"] ?? "";
        return new JObject
        {
            ["Code"] = 0,
            ["Message"] = $"mock accepted Op={op}",
            ["Op"] = op,
        };
    }

    private async Task QuietPipeLoopAsync(string pipeName, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream srv;
            try
            {
                srv = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch { return; }

            try { await srv.WaitForConnectionAsync(ct); }
            catch { srv.Dispose(); return; }

            _ = Task.Run(async () =>
            {
                using (srv)
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch { }
                }
            }, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await Task.WhenAll(_serverLoops).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { }
        _cts.Dispose();
    }
}
