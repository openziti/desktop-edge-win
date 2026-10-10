using System.IO.Pipes;
using System.Text;
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

    /// <summary>When set, the monitor's Start and Stop fail with this exception message.</summary>
    public string? ServiceActionFailure { get; set; }

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _serverLoops = new();
    private readonly object _dataLock = new();
    // Replaced on every Stop, since a cancelled source can't be reused for the next Start.
    private CancellationTokenSource _dataCts = new();
    private readonly List<CancellationTokenSource> _stoppedDataCts = new();
    private readonly List<Task> _dataLoops = new();
    // Never mutated, so concurrent serialization from the IPC and event threads is safe.
    private readonly JObject _landingStatus;
    private readonly object _recvLock = new();
    private readonly List<JObject> _receivedMonitor = new();
    private readonly List<JObject> _receivedData = new();

    public IReadOnlyList<JObject> ReceivedMonitorRequests
    {
        get { lock (_recvLock) return _receivedMonitor.ToArray(); }
    }

    public IReadOnlyList<JObject> ReceivedDataRequests
    {
        get { lock (_recvLock) return _receivedData.ToArray(); }
    }

    public MockIpcServer(string pipePrefix, JObject landingStatus)
    {
        PipePrefix = pipePrefix;
        _landingStatus = landingStatus;
    }

    public void Start()
    {
        StartDataPipes();
        StartMonitorPipes();
    }

    /// <summary>Serve only ZET's data pipes, for a session whose monitor is a real ZitiUpdateService.</summary>
    public void StartDataPipes()
    {
        lock (_dataLock)
        {
            CancellationToken ct = _dataCts.Token;
            _dataLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataIpcPipeName, PipeDirection.InOut,
                (srv, t) => ServeRequestsAsync(srv, AnswerDataRequest, t), ct)));
            _dataLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataEventPipeName, PipeDirection.Out,
                HandleDataEventClientAsync, ct)));
        }
    }

    /// <summary>Close the UI's data pipe clients and stop accepting new ones, as ZET does when its service stops.</summary>
    private void StopDataPipes()
    {
        lock (_dataLock)
        {
            _dataCts.Cancel();
            _stoppedDataCts.Add(_dataCts);
            _dataCts = new CancellationTokenSource();
        }
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

    /// <summary>Read one JSON request per line and write answer's reply.</summary>
    private static async Task ServeRequestsAsync(NamedPipeServerStream srv, Func<string, string> answer,
        CancellationToken ct)
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

                await writer.WriteLineAsync(answer(line));
            }
        }
    }

    private string AnswerDataRequest(string line)
    {
        try
        {
            JObject req = JObject.Parse(line);
            Step.UiSent(req);
            lock (_recvLock) _receivedData.Add(req);
            return BuildReply(req).ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            return new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = ex.Message }.ToString(Formatting.None);
        }
    }

    private string AnswerMonitorRequest(string line)
    {
        try
        {
            JObject req = JObject.Parse(line);
            lock (_recvLock) _receivedMonitor.Add(req);
            return BuildMonitorReply(req).ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            return new JObject { ["Code"] = 1, ["Message"] = ex.Message }.ToString(Formatting.None);
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

    private JObject BuildReply(JObject req)
    {
        string command = (string?)req["Command"] ?? "";
        if (command == "Status")
            return new JObject { ["Success"] = true, ["Code"] = 0, ["Data"] = _landingStatus };
        // Writes no dump files, so a feedback bundle carries none.
        if (command == "ZitiDump")
            return new JObject { ["Success"] = true, ["Code"] = 0 };
        // Fail loudly so a command the mock does not model shows up in the test instead of passing silently.
        return new JObject { ["Success"] = false, ["Code"] = 500, ["Error"] = $"mock IPC has no handler for command '{command}'" };
    }

    private async Task HandleDataEventClientAsync(NamedPipeServerStream srv, CancellationToken ct)
    {
        using (srv)
        using (StreamWriter writer = new StreamWriter(srv, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            // A new event client gets exactly one status message (ipc_event.c on_events_client).
            JObject statusPush = new JObject { ["Op"] = "status", ["Status"] = _landingStatus.DeepClone() };
            await writer.WriteLineAsync(statusPush.ToString(Formatting.None));
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { }
        }
    }

    private JObject BuildMonitorReply(JObject req)
    {
        // The monitor IPC takes {Op, Action} and answers a SvcResponse {Code, Message}. A Code 0 reply keeps the UI
        // moving for every op the tests drive.
        string op = (string?)req["Op"] ?? "";
        // The monitor's ServiceActions reply once the ziti service reaches the state, with that state as the Message.
        string? failure = ServiceActionFailure;
        if (op == "Stop" && (string?)req["Action"] == "Normal")
        {
            // A failed stop still kills ziti-edge-tunnel before the monitor replies (ServiceActions.StopService).
            StopDataPipes();
            return failure == null ? new JObject { ["Code"] = 0, ["Message"] = "Stopped" } : FailedServiceAction(failure);
        }
        if (op == "Start")
        {
            if (failure != null)
                return FailedServiceAction(failure);
            StartDataPipes();
            return new JObject { ["Code"] = 0, ["Message"] = "Running" };
        }
        return new JObject
        {
            ["Code"] = 0,
            ["Message"] = $"mock accepted Op={op}",
            ["Op"] = op,
        };
    }

    /// <summary>The reply IPCServer.processMessageAsync sends when a ServiceActions call throws with no inner exception.</summary>
    private static JObject FailedServiceAction(string exceptionMessage) => new JObject
    {
        ["Code"] = -2,
        ["Message"] = "FAILURE: " + exceptionMessage,
        ["Error"] = exceptionMessage + ":",
    };

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        Task[] dataLoops;
        lock (_dataLock)
        {
            _dataCts.Cancel();
            dataLoops = _dataLoops.ToArray();
        }
        // Loops return on cancel, so anything thrown here is a real pipe fault from earlier in the test.
        await Task.WhenAll(_serverLoops.Concat(dataLoops)).WaitAsync(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _dataCts.Dispose();
        foreach (CancellationTokenSource stopped in _stoppedDataCts) stopped.Dispose();
    }
}
