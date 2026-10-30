using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ZitiDesktopEdge.UITests.MockIpc;

internal static class IpcPipes
{
    public static async Task AcceptLoopAsync(string pipeName, PipeDirection direction,
        Func<NamedPipeServerStream, CancellationToken, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
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
            try { await pipe.WaitForConnectionAsync(ct); }
            catch (OperationCanceledException) { pipe.Dispose(); return; }

            _ = Task.Run(() => handler(pipe, ct), ct);
        }
    }

    public static string ClientProcessName(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeClientProcessId failed");
        using Process client = Process.GetProcessById((int)pid);
        return client.ProcessName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
}
