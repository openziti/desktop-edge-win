using System.Diagnostics;

namespace ZitiDesktopEdge.UITests.Drivers;

// URL enrollment has no JWT to vouch for the controller, so signer discovery and ZET fall back to OS trust.
public static class OsCaTrust
{
    private static readonly TimeSpan CertutilTimeout = TimeSpan.FromSeconds(30);

    // The quickstart root CA always has CN=root-ca.
    public static void Remove() => RunCertutil("-delstore", "Root", "root-ca");

    public static void RunCertutil(params string[] args)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "certutil",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in args) startInfo.ArgumentList.Add(argument);

        string command = "certutil " + string.Join(" ", args);
        using Process certutil = Process.Start(startInfo) ?? throw new InvalidOperationException($"{command} did not start");
        Task<string> stdout = certutil.StandardOutput.ReadToEndAsync();
        Task<string> stderr = certutil.StandardError.ReadToEndAsync();
        if (!certutil.WaitForExit(CertutilTimeout))
        {
            certutil.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} still running after {CertutilTimeout.TotalSeconds}s");
        }
        if (certutil.ExitCode != 0)
            throw new InvalidOperationException(
                $"{command} exited {certutil.ExitCode}. stdout: {stdout.Result} stderr: {stderr.Result}");
    }
}
