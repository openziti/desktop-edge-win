using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// A `ziti-edge-tunnel run` on its own -P pipe with an empty identity dir, beside the installed ZDEW's ZET.
/// </summary>
public sealed class ZetProcess : IAsyncDisposable
{
    // Outside the installed ZET's default 100.64.0.1/10, so both can tunnel at once.
    private const string DnsRange = "100.150.0.1/16";
    private static readonly TimeSpan PipeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The -I dir, where ZET writes each added identity's file.</summary>
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
            new[] { "run", "-I", identityDir, "-P", discriminator, "-d", DnsRange },
            new Dictionary<string, string>(),
            Path.Combine(home, "ziti-edge-tunnel.log"));

        ZetProcess zet = new ZetProcess(process, "ziti-edge-tunnel.sock." + discriminator, identityDir);
        try
        {
            await zet.WaitForPipeAsync(zet._ipcPipeName);
        }
        catch
        {
            await zet.DisposeAsync();
            throw;
        }
        return zet;
    }

    private async Task WaitForPipeAsync(string pipeName)
    {
        using NamedPipeClientStream probe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        Task connect = probe.ConnectAsync((int)PipeTimeout.TotalMilliseconds);
        while (!connect.IsCompleted)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"ziti-edge-tunnel exited before opening pipe '{pipeName}'. Log {_process.LogPath}:\n{_process.ReadLog()}");
            await Task.WhenAny(connect, Task.Delay(250));
        }
        try
        {
            await connect;
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"ziti-edge-tunnel opened no pipe '{pipeName}' in {PipeTimeout.TotalSeconds}s. Log: {_process.LogPath}", ex);
        }
    }

    /// <summary>
    /// Remove every identity ZET holds, straight over its command pipe, so a test that failed before forgetting its
    /// identity doesn't fail the next one too.
    /// </summary>
    public Task RemoveAllIdentitiesAsync() => OnPipeAsync(async (reader, writer) =>
    {
        JObject status = await SendAsync(reader, writer, new JObject { ["Command"] = "Status" });
        JArray identities = status["Data"]?["Identities"] as JArray ?? new JArray();
        foreach (JObject id in identities.OfType<JObject>())
        {
            await SendAsync(reader, writer, new JObject
            {
                ["Command"] = "RemoveIdentity",
                ["Data"] = new JObject { ["Identifier"] = id["Identifier"] },
            });
        }
    });

    /// <summary>Send one command straight over ZET's command pipe and return its reply, throwing unless it succeeds.</summary>
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
