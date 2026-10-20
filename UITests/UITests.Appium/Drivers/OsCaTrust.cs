using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

// URL enrollment has no JWT to vouch for the controller, so signer discovery and ZET fall back to OS trust.
public static class OsCaTrust
{
    // The quickstart root CA always has CN=root-ca.
    private const string RootCaName = "root-ca";
    private static readonly TimeSpan CertutilTimeout = TimeSpan.FromSeconds(30);

    public static void Install(string certPath) => RunCertutil("-addstore", "-f", "Root", certPath);

    public static void Remove() => RunCertutil("-delstore", "Root", RootCaName);

    private static void RunCertutil(params string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "certutil",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);

        string command = "certutil " + string.Join(" ", args);
        using Process p = Process.Start(psi) ?? throw new InvalidOperationException($"{command} did not start");
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(CertutilTimeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} still running after {CertutilTimeout.TotalSeconds}s");
        }
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"{command} exited {p.ExitCode}. stdout: {stdout.Result} stderr: {stderr.Result}");
    }
}
