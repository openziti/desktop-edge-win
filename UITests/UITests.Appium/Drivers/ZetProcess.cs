using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Drivers;

public sealed class ZetProcess : IAsyncDisposable
{
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(30);

    public string IdentityDir { get; }

    private readonly LoggedProcess _process;
    private readonly string _ipcPipeName;

    private ZetProcess(LoggedProcess process, string ipcPipeName, string identityDir)
    {
        _process = process;
        _ipcPipeName = ipcPipeName;
        IdentityDir = identityDir;
    }

    public static async Task<ZetProcess> StartAsync(string zetPath, string discriminator, string home)
    {
        using (WindowsIdentity user = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException(
                    "ziti-edge-tunnel run opens a TUN device, so run the integration tests from an elevated shell.");
        }
        if (!File.Exists(zetPath)) throw new FileNotFoundException($"ziti-edge-tunnel.exe not found at: {zetPath}");

        string identityDir = Path.Combine(home, "identities");
        Directory.CreateDirectory(identityDir);
        LoggedProcess process = LoggedProcess.Start(zetPath,
            // -d is outside the installed ZET's default 100.64.0.1/10, so both can tunnel at once.
            new[] { "run", "-I", identityDir, "-P", discriminator, "-d", "100.150.0.1/16" },
            new Dictionary<string, string>(),
            Path.Combine(home, "ziti-edge-tunnel.log"));

        string ipcPipeName = "ziti-edge-tunnel.sock." + discriminator;
        try
        {
            using NamedPipeClientStream probe = new NamedPipeClientStream(".", ipcPipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);
            Task connect = probe.ConnectAsync((int)PipeTimeout.TotalMilliseconds);
            while (!connect.IsCompleted)
            {
                if (process.HasExited)
                    throw new InvalidOperationException(
                        $"ziti-edge-tunnel exited before opening pipe '{ipcPipeName}'. Log {process.LogPath}:\n{process.ReadLog()}");
                await Task.WhenAny(connect, Task.Delay(250));
            }
            try
            {
                await connect;
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException(
                    $"ziti-edge-tunnel opened no pipe '{ipcPipeName}' in {PipeTimeout.TotalSeconds}s. Log: {process.LogPath}", ex);
            }
        }
        catch
        {
            await process.DisposeAsync();
            throw;
        }
        return new ZetProcess(process, ipcPipeName, identityDir);
    }

    // So a test that failed before forgetting its identity doesn't fail the next one too.
    public Task RemoveAllIdentitiesAsync() => OnPipeAsync(async (reader, writer) =>
    {
        JObject status = await SendAsync(reader, writer, new JObject { ["Command"] = "Status" });
        if (status["Data"]?["Identities"] is not JArray identities)
            throw new InvalidOperationException(
                $"ziti-edge-tunnel's Status reply has no Data.Identities array: {status.ToString(Formatting.None)}");
        foreach (JObject id in identities.OfType<JObject>())
        {
            await SendAsync(reader, writer, new JObject
            {
                ["Command"] = "RemoveIdentity",
                ["Data"] = new JObject { ["Identifier"] = id["Identifier"] },
            });
        }
    });

    public async Task<JObject> SendCommandAsync(JObject request)
    {
        JObject? reply = null;
        await OnPipeAsync(async (reader, writer) => reply = await SendAsync(reader, writer, request));
        return reply!;
    }

    private async Task OnPipeAsync(Func<StreamReader, StreamWriter, Task> use)
    {
        using NamedPipeClientStream pipe = new NamedPipeClientStream(".", _ipcPipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync((int)PipeTimeout.TotalMilliseconds);
        using StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 64 * 1024, leaveOpen: true);
        using StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false), 64 * 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        await use(reader, writer);
    }

    private static async Task<JObject> SendAsync(StreamReader reader, StreamWriter writer, JObject request)
    {
        await writer.WriteLineAsync(request.ToString(Formatting.None));
        string reply = await reader.ReadLineAsync()
            ?? throw new IOException($"ziti-edge-tunnel closed its pipe before answering {request.ToString(Formatting.None)}");
        JObject parsed = JObject.Parse(reply);
        if ((bool?)parsed["Success"] != true)
            throw new InvalidOperationException(
                $"ziti-edge-tunnel failed {request.ToString(Formatting.None)}: {reply}");
        return parsed;
    }

    public ValueTask DisposeAsync() => _process.DisposeAsync();
}
