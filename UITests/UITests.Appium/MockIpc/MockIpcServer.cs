using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

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

    private record Answer(string Reply, List<JObject> EventsAfterReply);

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
            }
        }
    }

    private Answer AnswerDataRequest(string line)
    {
        List<JObject> eventsAfterReply = new List<JObject>();
        try
        {
            JObject req = JObject.Parse(line);
            lock (_recvLock) _received.Add(req);
            Step.Log($"app sent {req["Command"]} to the mock");
            // serialized inside the lock: the Status reply holds _landingStatus itself
            lock (_landingStatusLock)
                return new Answer(BuildReply(req, eventsAfterReply).ToString(Formatting.None), eventsAfterReply);
        }
        catch (Exception ex)
        {
            return new Answer(
                new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = ex.Message }.ToString(Formatting.None),
                new List<JObject>());
        }
    }

    private Answer AnswerMonitorRequest(string line)
    {
        try
        {
            JObject req = JObject.Parse(line);
            lock (_recvLock) _receivedMonitor.Add(req);
            return new Answer(BuildMonitorReply(req).ToString(Formatting.None), new List<JObject>());
        }
        catch (Exception ex)
        {
            return new Answer(new JObject { ["Code"] = 1, ["Message"] = ex.Message }.ToString(Formatting.None),
                new List<JObject>());
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
    private JObject BuildReply(JObject req, List<JObject> eventsAfterReply)
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

    private JObject HandleIdentityOnOff(JObject data, List<JObject> eventsAfterReply)
    {
        string identifier = (string?)data["Identifier"] ?? "";
        bool onOff = (bool?)data["OnOff"] ?? false;

        // Cached so later Status queries match.
        JObject? id = FindIdentity(identifier);
        if (id != null)
        {
            id["Active"] = onOff;

            // Captured from ZET 1.19 after the reply. Off: identity "added" with Active=false, then controller
            // "disconnected". On: identity "added" then controller "connected".
            eventsAfterReply.Add(new JObject
            {
                ["Op"] = "identity",
                ["Action"] = "added",
                ["Fingerprint"] = id["FingerPrint"],
                ["Id"] = id.DeepClone(),
            });
            eventsAfterReply.Add(new JObject
            {
                ["Op"] = "controller",
                ["Action"] = onOff ? "connected" : "disconnected",
                ["Identifier"] = id["Identifier"],
                ["Fingerprint"] = id["FingerPrint"],
            });
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
