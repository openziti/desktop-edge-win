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
    // Mutated by command handlers on IPC threads and read by event clients: touch it only under _landingStatusLock.
    private readonly JObject _landingStatus;
    private readonly object _landingStatusLock = new();
    private readonly object _recvLock = new();
    private readonly List<JObject> _received = new();
    private readonly List<JObject> _receivedMonitor = new();
    // One queue per connected event client, like ZET broadcasting to every client. Pushes with no client connected wait
    // in _pendingEvents for the next one.
    private readonly List<Channel<JObject>> _eventClients = new();
    private readonly List<JObject> _pendingEvents = new();
    private readonly object _eventClientsLock = new();

    // Base32 secret per identity from EnableMFA. VerifyMFA checks codes against it with real RFC 6238 TOTP.
    private readonly Dictionary<string, string> _mfaSecrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mfaSecretsLock = new();

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

    /// <summary>
    /// Stand-in for a finished external-auth login, since the real Authenticate button opens a browser with
    /// Process.Start. Emits what ZET sends after the login: an identity "added" event with NeedsExtAuth=false.
    /// </summary>
    public void PushExtAuthSuccess(string identifier)
    {
        JObject evt;
        lock (_landingStatusLock)
        {
            JObject? id = FindIdentity(identifier);
            if (id == null) throw new InvalidOperationException($"PushExtAuthSuccess: no identity '{identifier}' in fixture.");

            // so later Status queries match the post-login state
            id["NeedsExtAuth"] = false;

            evt = new JObject
            {
                ["Op"] = "identity",
                ["Action"] = "added",
                ["Fingerprint"] = id["FingerPrint"],
                ["Id"] = id.DeepClone(),
            };
        }
        PushEvent(evt);
    }

    private void PushEvent(JObject evt)
    {
        lock (_eventClientsLock)
        {
            if (_eventClients.Count == 0)
            {
                _pendingEvents.Add(evt);
                return;
            }
            foreach (Channel<JObject> client in _eventClients) client.Writer.TryWrite(evt);
        }
    }

    public MockIpcServer(string pipePrefix, JObject landingStatus)
    {
        PipePrefix = pipePrefix;
        _landingStatus = landingStatus;
    }

    /// <summary>Case-insensitive, like ZET's identifier lookups. Call under _landingStatusLock.</summary>
    private JObject? FindIdentity(string identifier) =>
        (_landingStatus["Identities"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault(i => string.Equals((string?)i["Identifier"], identifier, StringComparison.OrdinalIgnoreCase));

    /// <summary>The gate for a submitted MFA code. Callers decide what an empty code means.</summary>
    private bool IsCodeValid(string identifier, string code)
    {
        if (code == RejectedMfaCode) return false;
        if (code == AcceptedMfaCode) return true;
        string? secret;
        lock (_mfaSecretsLock) _mfaSecrets.TryGetValue(identifier, out secret);
        return secret != null && Totp.Validate(secret, code);
    }

    public void Start()
    {
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataIpcPipeName, PipeDirection.InOut,
            (srv, ct) => ServeRequestsAsync(srv, AnswerDataRequest, ct), _cts.Token)));
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataEventPipeName, PipeDirection.Out,
            HandleDataEventClientAsync, _cts.Token)));
        StartMonitorPipes();
    }

    /// <summary>Serve only the monitor pipes, for a session whose data pipes go to a real ZET through RelayIpcServer.</summary>
    public void StartMonitorPipes()
    {
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(MonitorIpcPipeName, PipeDirection.InOut,
            (srv, ct) => ServeRequestsAsync(srv, AnswerMonitorRequest, ct), _cts.Token)));
        // The UI connects to the monitor's event pipe but the tests never push monitor events.
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(MonitorEventPipeName, PipeDirection.InOut,
            HoldOpenAsync, _cts.Token)));
    }

    /// <summary>
    /// EventsAfterStatusSave go out StatusFileSaveMs after EventsAfterReply: ZET writes its status file between the two
    /// on a successful MFA change (ziti-edge-tunnel.c, TunnelEvent_MFAStatusEvent), about 35ms in a 1.19 capture.
    /// </summary>
    private record Answer(string Reply, List<JObject> EventsAfterReply, List<JObject> EventsAfterStatusSave);

    private const int StatusFileSaveMs = 50;

    /// <summary>Read one JSON request per line and write answer's reply, then push its follow-up events.</summary>
    private async Task ServeRequestsAsync(NamedPipeServerStream srv, Func<string, Answer> answer, CancellationToken ct)
    {
        using (srv)
        using (StreamReader reader = new StreamReader(srv, new UTF8Encoding(false), false, 16 * 1024, leaveOpen: true))
        using (StreamWriter writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            while (srv.IsConnected && !ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync().WaitAsync(ct); }
                // the app closed its end of the pipe, or the mock is shutting down
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { return; }
                if (line == null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                Answer reply = answer(line);
                await writer.WriteLineAsync(reply.Reply);
                foreach (JObject evt in reply.EventsAfterReply) PushEvent(evt);
                if (reply.EventsAfterStatusSave.Count > 0)
                {
                    await Task.Delay(StatusFileSaveMs, ct);
                    foreach (JObject evt in reply.EventsAfterStatusSave) PushEvent(evt);
                }
            }
        }
    }

    private Answer AnswerDataRequest(string line)
    {
        List<JObject> eventsAfterReply = new List<JObject>();
        List<JObject> eventsAfterStatusSave = new List<JObject>();
        try
        {
            JObject req = JObject.Parse(line);
            lock (_recvLock) _received.Add(req);
            // serialized inside the lock: the Status reply holds _landingStatus itself
            lock (_landingStatusLock)
                return new Answer(BuildReply(req, eventsAfterReply, eventsAfterStatusSave).ToString(Formatting.None),
                    eventsAfterReply, eventsAfterStatusSave);
        }
        catch (Exception ex)
        {
            return new Answer(
                new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = ex.Message }.ToString(Formatting.None),
                new List<JObject>(), new List<JObject>());
        }
    }

    private Answer AnswerMonitorRequest(string line)
    {
        try
        {
            JObject req = JObject.Parse(line);
            lock (_recvLock) _receivedMonitor.Add(req);
            return new Answer(BuildMonitorReply(req).ToString(Formatting.None), new List<JObject>(), new List<JObject>());
        }
        catch (Exception ex)
        {
            return new Answer(new JObject { ["Code"] = 1, ["Message"] = ex.Message }.ToString(Formatting.None),
                new List<JObject>(), new List<JObject>());
        }
    }

    private static async Task HoldOpenAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Reply to one command. Handlers add to eventsAfterReply what ZET sends once the reply is out, and the caller
    /// pushes those after writing the reply.
    /// </summary>
    private JObject BuildReply(JObject req, List<JObject> eventsAfterReply, List<JObject> eventsAfterStatusSave)
    {
        string command = (string?)req["Command"] ?? "";
        JObject data = req["Data"] as JObject ?? new JObject();

        return command switch
        {
            "Status" => new JObject
            {
                ["Success"] = true,
                ["Code"] = 0,
                ["Data"] = _landingStatus,
            },
            "IdentityOnOff" => HandleIdentityOnOff(data, eventsAfterReply),
            "ExternalAuth" => HandleExternalAuth(data),
            "UpdateInterfaceConfig" => HandleUpdateInterfaceConfig(data),
            "EnableMFA" => HandleEnableMFA(data, eventsAfterReply),
            "VerifyMFA" => HandleMfaResult(data, "enrollment_verification", eventsAfterReply, eventsAfterStatusSave),
            "RemoveMFA" => HandleMfaResult(data, "enrollment_remove", eventsAfterReply, eventsAfterStatusSave),
            "SubmitMFA" => HandleMfaResult(data, "mfa_auth_status", eventsAfterReply, eventsAfterStatusSave),
            "GenerateMFACodes" => HandleGenerateMFACodes(data),
            "RemoveIdentity" => HandleRemoveIdentity(data),
            "SetLogLevel" => new JObject { ["Success"] = true, ["Code"] = 0 },
            // Fail loudly so a command the mock does not model shows up in the test instead of passing silently.
            _ => new JObject { ["Success"] = false, ["Code"] = 500, ["Error"] = $"mock IPC has no handler for command '{command}'" },
        };
    }

    private JObject HandleRemoveIdentity(JObject data)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        JObject? id = FindIdentity(identifier);
        if (id == null)
            return new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = $"mock has no identity '{identifier}'" };
        // so later Status queries no longer list it
        id.Remove();
        return new JObject { ["Success"] = true, ["Code"] = 0 };
    }

    /// <summary>
    /// AcceptedMfaCode always passes, for flows with no known secret, and RejectedMfaCode always fails. Any other
    /// code is checked as real TOTP against the secret from EnableMFA.
    /// </summary>
    public const string AcceptedMfaCode = "123456";
    public const string RejectedMfaCode = "666666";

    private JObject HandleMfaResult(JObject data, string action, List<JObject> eventsAfterReply,
        List<JObject> eventsAfterStatusSave)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        string code = (string?)data["Code"] ?? "";
        JObject? ident = FindIdentity(identifier);
        string fingerprint = ident?["FingerPrint"]?.ToString() ?? "MOCKFP";

        // An empty code is the enrollment path, which carries no token yet.
        bool successful = string.IsNullOrEmpty(code) || IsCodeValid(identifier, code);

        JObject evt = new JObject
        {
            ["Op"] = "mfa",
            ["Action"] = action,
            ["Identifier"] = identifier,
            ["Fingerprint"] = fingerprint,
            ["Successful"] = successful,
        };
        if (!successful)
        {
            // the controller's message, as ZET 1.19 relays it
            evt["Error"] = "the token provided was invalid";
        }

        // Like ZET's TunnelEvent_MFAStatusEvent: a verified or submitted code marks MFA enabled and satisfied, and an
        // identity "updated" event goes out before the mfa event.
        JObject? updated = null;
        if (successful && (action == "enrollment_verification" || action == "mfa_auth_status"))
        {
            if (ident != null)
            {
                ident["MfaEnabled"] = true;
                ident["MfaNeeded"] = false;
                updated = new JObject
                {
                    ["Op"] = "identity",
                    ["Action"] = "updated",
                    ["Fingerprint"] = ident["FingerPrint"],
                    ["Id"] = ident.DeepClone(),
                };
            }
        }
        if (successful && action == "enrollment_remove")
        {
            if (ident != null)
            {
                ident["MfaEnabled"] = false;
                ident["MfaNeeded"] = false;
            }
        }

        if (updated != null) eventsAfterReply.Add(updated);
        // A successful change saves the status file before the mfa event, a failure sends it straight away.
        if (successful && ident != null)
        {
            eventsAfterStatusSave.Add(evt);
        }
        else
        {
            eventsAfterReply.Add(evt);
        }

        // ZET 1.19 fails a bad code with Code 500 and the controller's message.
        return successful
            ? new JObject { ["Success"] = true, ["Code"] = 0 }
            : new JObject { ["Success"] = false, ["Code"] = 500, ["Error"] = (string?)evt["Error"] };
    }

    private JObject HandleGenerateMFACodes(JObject data)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        string code = (string?)data["Code"] ?? "";

        // Unlike HandleMfaResult, an empty code fails: regenerating always needs one.
        bool valid = !string.IsNullOrEmpty(code) && IsCodeValid(identifier, code);

        if (!valid)
        {
            return new JObject
            {
                ["Success"] = false,
                ["Code"] = 1,
                ["Error"] = "Invalid MFA code for regenerate.",
            };
        }

        // REGEN-prefixed so a test can tell regenerated codes from the initial set.
        JArray newCodes = new JArray();
        Random rnd = new Random();
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
        string identifier = (string?)data["Identifier"] ?? "";
        string provider = (string?)data["Provider"] ?? "mock-provider";
        string fakeUrl = $"https://idp.example/auth?provider={provider}&state=MOCKSTATE&redirect=http://localhost:54321/auth/callback";
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
        // Cached so later Status queries return the saved values.
        JObject? l3 = data["L3"] as JObject;
        JObject? l2 = data["L2"] as JObject;
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

    private JObject HandleEnableMFA(JObject data, List<JObject> eventsAfterReply)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        string fingerprint = FindIdentity(identifier)?["FingerPrint"]?.ToString() ?? "MOCKFP";

        string secret = Totp.GenerateSecret();
        lock (_mfaSecretsLock) _mfaSecrets[identifier] = secret;

        string url = $"otpauth://totp/openziti.io:{Uri.EscapeDataString(identifier)}?issuer=openziti.io&secret={secret}";
        JArray codes = new JArray(
            "AAAAAA", "BBBBBB", "CCCCCC", "DDDDDD", "EEEEEE",
            "FFFFFF", "GGGGGG", "HHHHHH", "IIIIII", "JJJJJJ",
            "KKKKKK", "LLLLLL", "MMMMMM", "NNNNNN", "OOOOOO",
            "PPPPPP", "QQQQQQ", "RRRRRR", "SSSSSS", "TTTTTT");

        // The UI opens the QR dialog on this event, not on the reply. ZET 1.19 sends it after the reply.
        JObject challenge = new JObject
        {
            ["Op"] = "mfa",
            ["Action"] = "enrollment_challenge",
            ["Identifier"] = identifier,
            ["Fingerprint"] = fingerprint,
            ["Successful"] = true,
            ["ProvisioningUrl"] = url,
            ["RecoveryCodes"] = codes,
        };
        eventsAfterReply.Add(challenge);

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

    private JObject HandleIdentityOnOff(JObject data, List<JObject> eventsAfterReply)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        bool onOff = (bool?)data["OnOff"] ?? false;

        // Cached so later Status queries match.
        JObject? id = FindIdentity(identifier);
        if (id != null)
        {
            id["Active"] = onOff;
            if (!onOff) id["MfaNeeded"] = false;

            // Captured from ZET 1.19 after the reply. Off: identity "added" with Active=false, then controller
            // "disconnected". On: identity "added" then controller "connected", except that an MFA identity
            // re-authenticates instead, which ZET reports as a status event then an mfa auth_challenge event.
            List<JObject> events = new List<JObject>();
            if (onOff && (bool?)id["MfaEnabled"] == true)
            {
                id["MfaNeeded"] = true;
                events.Add(new JObject { ["Op"] = "status", ["Status"] = _landingStatus.DeepClone() });
                events.Add(new JObject
                {
                    ["Op"] = "mfa",
                    ["Action"] = "auth_challenge",
                    ["Identifier"] = id["Identifier"],
                    ["Fingerprint"] = id["FingerPrint"],
                    ["Successful"] = false,
                });
            }
            else
            {
                events.Add(new JObject
                {
                    ["Op"] = "identity",
                    ["Action"] = "added",
                    ["Fingerprint"] = id["FingerPrint"],
                    ["Id"] = id.DeepClone(),
                });
                events.Add(new JObject
                {
                    ["Op"] = "controller",
                    ["Action"] = onOff ? "connected" : "disconnected",
                    ["Identifier"] = id["Identifier"],
                    ["Fingerprint"] = id["FingerPrint"],
                });
            }
            eventsAfterReply.AddRange(events);
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

    private async Task HandleDataEventClientAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        using (StreamWriter writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            // Like ZET, a new event client gets exactly one status message (ipc_event.c on_events_client).
            JObject statusPush;
            lock (_landingStatusLock) statusPush = new JObject { ["Op"] = "status", ["Status"] = _landingStatus.DeepClone() };
            await writer.WriteLineAsync(statusPush.ToString(Formatting.None));

            Channel<JObject> events = Channel.CreateUnbounded<JObject>();
            lock (_eventClientsLock)
            {
                foreach (JObject pending in _pendingEvents) events.Writer.TryWrite(pending);
                _pendingEvents.Clear();
                _eventClients.Add(events);
            }
            try
            {
                await foreach (JObject evt in events.Reader.ReadAllAsync(ct))
                {
                    await writer.WriteLineAsync(evt.ToString(Formatting.None));
                }
            }
            catch (OperationCanceledException) { }
            // the UI dropped this pipe: other clients got their own copy of every event
            catch (IOException) { }
            finally
            {
                lock (_eventClientsLock) _eventClients.Remove(events);
            }
        }
    }

    private JObject BuildMonitorReply(JObject req)
    {
        // The monitor IPC takes {Op, Action} and answers a SvcResponse {Code, Message}. A Code 0 reply keeps the UI
        // moving for every op the tests drive.
        string op = (string?)req["Op"] ?? "";
        return new JObject
        {
            ["Code"] = 0,
            ["Message"] = $"mock accepted Op={op}",
            ["Op"] = op,
        };
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        // Loops return on cancel, so anything thrown here is a real pipe fault from earlier in the test.
        await Task.WhenAll(_serverLoops).WaitAsync(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
