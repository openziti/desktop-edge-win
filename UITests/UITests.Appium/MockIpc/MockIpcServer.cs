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

    public string? ServiceActionFailure { get; set; }

    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<Task> _serverLoops = new();
    private readonly object _dataLock = new();
    // Replaced on every Stop, since a cancelled source can't be reused for the next Start.
    private CancellationTokenSource _dataPipesShutdown = new();
    private readonly List<CancellationTokenSource> _stoppedDataPipes = new();
    private readonly List<Task> _dataLoops = new();
    // Never mutated, so concurrent serialization from the IPC and event threads is safe.
    private readonly JObject _landingStatus;
    private readonly object _receivedLock = new();
    private readonly List<JObject> _receivedMonitor = new();
    private readonly List<JObject> _receivedData = new();

    public IReadOnlyList<JObject> ReceivedMonitorRequests
    {
        get { lock (_receivedLock) return _receivedMonitor.ToArray(); }
    }

    public IReadOnlyList<JObject> ReceivedDataRequests
    {
        get { lock (_receivedLock) return _receivedData.ToArray(); }
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

    public void StartDataPipes()
    {
        lock (_dataLock)
        {
            CancellationToken dataPipesToken = _dataPipesShutdown.Token;
            _dataLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataIpcPipeName, PipeDirection.InOut,
                (ui, ct) =>
                {
                    string sender = IpcPipes.ClientProcessName(ui);
                    return ServeRequestsAsync(ui, line =>
                    {
                        try
                        {
                            JObject request = JObject.Parse(line);
                            Step.Sent(sender, request);
                            lock (_receivedLock) _receivedData.Add(request);
                            string command = (string?)request["Command"] ?? "";
                            if (command == "Status")
                                return new JObject { ["Success"] = true, ["Code"] = 0, ["Data"] = _landingStatus }.ToString(Formatting.None);
                            // Writes no dump files, so a feedback bundle carries none.
                            if (command == "ZitiDump")
                                return new JObject { ["Success"] = true, ["Code"] = 0 }.ToString(Formatting.None);
                            // Fail loudly so a command the mock does not model shows up in the test instead of passing silently.
                            return new JObject { ["Success"] = false, ["Code"] = 500, ["Error"] = $"mock IPC has no handler for command '{command}'" }.ToString(Formatting.None);
                        }
                        catch (Exception ex)
                        {
                            return new JObject { ["Success"] = false, ["Code"] = 1, ["Error"] = ex.Message }.ToString(Formatting.None);
                        }
                    }, ct);
                }, dataPipesToken)));
            _dataLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataEventPipeName, PipeDirection.Out,
                async (ui, ct) =>
                {
                    using (ui)
                    using (StreamWriter writer = new StreamWriter(ui, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                    {
                        // A new event client gets exactly one status message (ipc_event.c on_events_client).
                        JObject statusPush = new JObject { ["Op"] = "status", ["Status"] = _landingStatus.DeepClone() };
                        await writer.WriteLineAsync(statusPush.ToString(Formatting.None));
                        try { await Task.Delay(Timeout.Infinite, ct); }
                        catch (OperationCanceledException) { }
                    }
                }, dataPipesToken)));
        }
    }

    public void StartMonitorPipes()
    {
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(MonitorIpcPipeName, PipeDirection.InOut,
            (ui, ct) => ServeRequestsAsync(ui, line =>
            {
                try
                {
                    JObject request = JObject.Parse(line);
                    lock (_receivedLock) _receivedMonitor.Add(request);
                    string operation = (string?)request["Op"] ?? "";
                    // The monitor's ServiceActions reply once the ziti service reaches the state, with that state as the Message.
                    string? failure = ServiceActionFailure;
                    if (operation == "Stop" && (string?)request["Action"] == "Normal")
                    {
                        // A failed stop still kills ziti-edge-tunnel before the monitor replies (ServiceActions.StopService).
                        lock (_dataLock)
                        {
                            _dataPipesShutdown.Cancel();
                            _stoppedDataPipes.Add(_dataPipesShutdown);
                            _dataPipesShutdown = new CancellationTokenSource();
                        }
                        if (failure != null)
                            return FailedServiceAction(failure).ToString(Formatting.None);
                        return new JObject { ["Code"] = 0, ["Message"] = "Stopped" }.ToString(Formatting.None);
                    }
                    if (operation == "Start")
                    {
                        if (failure != null)
                            return FailedServiceAction(failure).ToString(Formatting.None);
                        StartDataPipes();
                        return new JObject { ["Code"] = 0, ["Message"] = "Running" }.ToString(Formatting.None);
                    }
                    // A Code 0 reply keeps the UI moving for every op the tests drive.
                    return new JObject { ["Code"] = 0, ["Message"] = $"mock accepted Op={operation}", ["Op"] = operation }.ToString(Formatting.None);
                }
                catch (Exception ex)
                {
                    return new JObject { ["Code"] = 1, ["Message"] = ex.Message }.ToString(Formatting.None);
                }
            }, ct), _shutdown.Token)));
        // The UI connects to the monitor's event pipe but the tests never push monitor events.
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(MonitorEventPipeName, PipeDirection.InOut,
            async (ui, ct) =>
            {
                using (ui)
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { }
                }
            }, _shutdown.Token)));
    }

    private static async Task ServeRequestsAsync(NamedPipeServerStream pipe, Func<string, string> answer,
        CancellationToken ct)
    {
        using (pipe)
        using (StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 16 * 1024, leaveOpen: true))
        using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync().WaitAsync(ct); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { return; }
                if (line == null) return;
                if (string.IsNullOrWhiteSpace(line)) continue;

                await writer.WriteLineAsync(answer(line));
            }
        }
    }

    // IPCServer.processMessageAsync's reply when a ServiceActions call throws with no inner exception.
    private static JObject FailedServiceAction(string exceptionMessage) => new JObject
    {
        ["Code"] = -2,
        ["Message"] = "FAILURE: " + exceptionMessage,
        ["Error"] = exceptionMessage + ":",
    };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        Task[] dataLoops;
        lock (_dataLock)
        {
            _dataPipesShutdown.Cancel();
            dataLoops = _dataLoops.ToArray();
        }
        // Loops return on cancel, so anything thrown here is a real pipe fault from earlier in the test.
        await Task.WhenAll(_serverLoops.Concat(dataLoops)).WaitAsync(TimeSpan.FromSeconds(2));
        _shutdown.Dispose();
        _dataPipesShutdown.Dispose();
        foreach (CancellationTokenSource stopped in _stoppedDataPipes) stopped.Dispose();
    }
}
