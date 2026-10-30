using System.Net;
using System.Net.Sockets;
using System.Text;
using Step = ZitiDesktopEdge.UITests.Tests.Step;

namespace ZitiDesktopEdge.UITests.Drivers;

public sealed class ReleaseStreamServer : IAsyncDisposable
{
    // Fixed, because the Automatic Upgrades screen the baselines capture shows the URL.
    private const int Port = 47380;
    private const string HeadEnd = "\r\n\r\n";

    private readonly TcpListener _listener;
    private readonly byte[] _response;
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly Task _serving;
    private int _requests;

    public string Url => $"http://127.0.0.1:{Port}/stable.json";

    public int Requests => Volatile.Read(ref _requests);

    private ReleaseStreamServer(TcpListener listener, string json)
    {
        _listener = listener;
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close{HeadEnd}");
        _response = head.Concat(body).ToArray();
        _serving = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                using (client)
                {
                    NetworkStream stream = client.GetStream();
                    // The monitor only sends GETs, which carry no body.
                    StringBuilder head = new StringBuilder();
                    byte[] buffer = new byte[1024];
                    while (!head.ToString().Contains(HeadEnd))
                    {
                        int read = await stream.ReadAsync(buffer, _stop.Token);
                        if (read == 0)
                            throw new IOException($"the client closed the connection mid request after: {head}");
                        head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }
                    Step.Log($"release stream server answered {head.ToString().Split("\r\n")[0]}");
                    await stream.WriteAsync(_response, _stop.Token);
                    Interlocked.Increment(ref _requests);
                }
            }
        });
    }

    public static ReleaseStreamServer Start(string json)
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"the release stream server could not listen on 127.0.0.1:{Port}", ex);
        }
        return new ReleaseStreamServer(listener, json);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _serving;
        _stop.Dispose();
    }
}
