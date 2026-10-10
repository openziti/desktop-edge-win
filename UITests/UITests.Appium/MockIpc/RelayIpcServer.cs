using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

namespace ZitiDesktopEdge.UITests.MockIpc;

/// <summary>
/// Hosts the prefixed data pipes the UI dials and forwards every line to a real ziti-edge-tunnel started with
/// -P &lt;discriminator&gt;, recording each line in arrival order. Disposing writes the recording to capturePath as
/// JSON lines. The monitor pipes stay on MockIpcServer.
/// </summary>
public sealed class RelayIpcServer : IAsyncDisposable
{
    public sealed record RecordedLine(string From, string Pipe, string Line, DateTime ReceivedAtUtc);

    private static readonly TimeSpan ZetConnectTimeout = TimeSpan.FromSeconds(5);
    // ZET usually sends mfa_auth_status about 35ms after its SubmitMFA reply, but sometimes in the same millisecond.
    // MFAScreen.DoAuthenticate sets IsMFANeeded on the reply, so an event handled first leaves the lock stuck.
    private static readonly TimeSpan MfaAuthStatusDelay = TimeSpan.FromMilliseconds(35);

    public string PipePrefix { get; }
    public string ZetDiscriminator { get; }
    public string CapturePath { get; }
    public string DataIpcPipeName => PipePrefix + "ziti-edge-tunnel.sock";
    public string DataEventPipeName => PipePrefix + "ziti-edge-tunnel-event.sock";
    private string ZetIpcPipeName => "ziti-edge-tunnel.sock." + ZetDiscriminator;
    private string ZetEventPipeName => "ziti-edge-tunnel-event.sock." + ZetDiscriminator;

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _serverLoops = new();
    private readonly List<RecordedLine> _recorded = new();
    private readonly object _recordedLock = new();
    private readonly List<Exception> _faults = new();
    private readonly object _faultsLock = new();

    public IReadOnlyList<RecordedLine> Recorded
    {
        get { lock (_recordedLock) return _recorded.ToArray(); }
    }

    public RelayIpcServer(string pipePrefix, string zetDiscriminator, string capturePath)
    {
        PipePrefix = pipePrefix;
        ZetDiscriminator = zetDiscriminator;
        CapturePath = capturePath;
    }

    public async Task StartAsync()
    {
        // Fail before the UI launches: a ZET that isn't running otherwise only shows up as a UI that never connects.
        using (NamedPipeClientStream probe = await ConnectToZetAsync(ZetIpcPipeName, PipeDirection.InOut, _cts.Token)) { }

        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataIpcPipeName, PipeDirection.InOut,
            RelayCommandClientAsync, _cts.Token)));
        _serverLoops.Add(Task.Run(() => IpcPipes.AcceptLoopAsync(DataEventPipeName, PipeDirection.Out,
            RelayEventClientAsync, _cts.Token)));
    }

    private async Task RelayCommandClientAsync(NamedPipeServerStream ui, CancellationToken ct)
    {
        try
        {
            using (ui)
            using (NamedPipeClientStream zet = await ConnectToZetAsync(ZetIpcPipeName, PipeDirection.InOut, ct))
            {
                Task toZet = PumpAsync(ui, zet, "ui", "cmd", ct);
                Task toUi = PumpAsync(zet, ui, "zet", "cmd", ct);
                await Task.WhenAny(toZet, toUi);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_faultsLock) _faults.Add(ex);
        }
    }

    private async Task RelayEventClientAsync(NamedPipeServerStream ui, CancellationToken ct)
    {
        try
        {
            using (ui)
            using (NamedPipeClientStream zet = await ConnectToZetAsync(ZetEventPipeName, PipeDirection.In, ct))
            {
                await PumpAsync(zet, ui, "zet", "event", ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_faultsLock) _faults.Add(ex);
        }
    }

    /// <summary>Copy lines from one pipe to the other until either end closes, recording each non-blank line.</summary>
    private async Task PumpAsync(Stream source, Stream target, string from, string pipe, CancellationToken ct)
    {
        StreamReader reader = new StreamReader(source, new UTF8Encoding(false), false, 64 * 1024, leaveOpen: true);
        StreamWriter writer = new StreamWriter(target, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            // one end closed its pipe, or the relay is shutting down
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { return; }
            if (line == null) return;

            if (!string.IsNullOrWhiteSpace(line))
            {
                lock (_recordedLock) _recorded.Add(new RecordedLine(from, pipe, line, DateTime.UtcNow));
                JObject json = JObject.Parse(line);
                if (from == "ui") Step.UiSent(json);
                else if (pipe == "cmd") Step.ZetReplied(json);
                // metrics arrives every few seconds and would bury the step lines
                else if ((string?)json["Op"] != "metrics") Step.ZetEvent(json);
                // Not cancellable: a cancelled delay would fault the pump instead of ending it.
                if (pipe == "event" && (string?)json["Action"] == "mfa_auth_status")
                    await Task.Delay(MfaAuthStatusDelay);
            }
            try { await writer.WriteLineAsync(line); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return; }
        }
    }

    private static async Task<NamedPipeClientStream> ConnectToZetAsync(string pipeName, PipeDirection direction,
        CancellationToken ct)
    {
        NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, direction, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)ZetConnectTimeout.TotalMilliseconds, ct);
        }
        catch (TimeoutException ex)
        {
            pipe.Dispose();
            throw new TimeoutException(
                $"ziti-edge-tunnel pipe '{pipeName}' accepted no connection within {ZetConnectTimeout.TotalSeconds}s. " +
                "Start it elevated with: ziti-edge-tunnel run -I <empty dir> -P <discriminator>", ex);
        }
        return pipe;
    }

    private async Task WriteCaptureAsync()
    {
        StringBuilder capture = new StringBuilder();
        foreach (RecordedLine r in Recorded)
        {
            JToken message;
            try { message = JToken.Parse(r.Line); }
            catch (JsonReaderException ex)
            {
                throw new InvalidDataException($"{r.From} sent a line on the {r.Pipe} pipe that is not JSON: {r.Line}", ex);
            }
            JObject entry = new JObject { ["from"] = r.From, ["pipe"] = r.Pipe, ["message"] = message };
            capture.Append(entry.ToString(Formatting.None)).Append('\n');
        }
        Directory.CreateDirectory(Path.GetDirectoryName(CapturePath)!);
        await File.WriteAllTextAsync(CapturePath, capture.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Task.WhenAll(_serverLoops).WaitAsync(TimeSpan.FromSeconds(2));
        _cts.Dispose();

        // Written before faults are raised, so a failed run still leaves its recording.
        await WriteCaptureAsync();

        Exception[] faults;
        lock (_faultsLock) faults = _faults.ToArray();
        if (faults.Length > 0)
            throw new AggregateException($"relay to ziti-edge-tunnel -P {ZetDiscriminator} faulted", faults);
    }
}
