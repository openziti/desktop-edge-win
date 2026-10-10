using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace ZitiDesktopEdge.UITests.Drivers;

/// <summary>
/// The quickstart root CA in the machine's trusted root store. URL enrollment has no JWT to vouch for the controller, so both the app's signer discovery and ZET fall back to OS trust. Needs an
/// elevated process: LocalMachine Root rejects anyone else.
/// </summary>
public static class OsCaTrust
{
    // The quickstart root CA always has CN=root-ca.
    private const string RootCaName = "root-ca";
    private static readonly TimeSpan CertutilTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a CN=root-ca certificate is in LocalMachine Root. Probing the controller instead would miss a CA left by
    /// a crashed run whose quickstart had different keys.
    /// </summary>
    public static bool IsInstalled()
    {
        using X509Store store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, $"CN={RootCaName}", false).Count > 0;
    }

    public static void Install(string certPath) => RunCertutil("-addstore", "-f", "Root", certPath);

    /// <summary>Removes every CN=root-ca certificate from LocalMachine Root.</summary>
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
