using System.IO.Pipes;

namespace ZitiDesktopEdge.UITests.MockIpc;

/// <summary>Named pipe server plumbing shared by MockIpcServer and RelayIpcServer.</summary>
internal static class IpcPipes
{
    /// <summary>Accept clients on one pipe until cancelled, each served by handler on its own task.</summary>
    public static async Task AcceptLoopAsync(string pipeName, PipeDirection direction,
        Func<NamedPipeServerStream, CancellationToken, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream srv = CreateServerPipe(pipeName, direction);
            try { await srv.WaitForConnectionAsync(ct); }
            catch (OperationCanceledException) { srv.Dispose(); return; }

            _ = Task.Run(() => handler(srv, ct), ct);
        }
    }

    private static NamedPipeServerStream CreateServerPipe(string pipeName, PipeDirection direction)
    {
        try
        {
            return new NamedPipeServerStream(
                pipeName,
                direction,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Test IPC could not create pipe '{pipeName}': {ex.Message}", ex);
        }
    }
}
