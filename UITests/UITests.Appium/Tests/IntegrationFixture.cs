using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// A quickstart controller, a dex IdP and a ZET on ZetDiscriminator, shared by every test in IntegrationCollection.
/// All run from TestResults\&lt;home name&gt;, which keeps their logs after the run.
/// </summary>
public class IntegrationFixture : IAsyncLifetime
{
    public const string ZetDiscriminator = "zdew-ui-test";
    public const string WorkingSignerName = "test_ext_auth_signer_working";

    public string Home { get; }
    public Quickstart Quickstart => _quickstart ?? throw new InvalidOperationException("quickstart not started");
    public Dex Dex => _dex ?? throw new InvalidOperationException("dex not started");
    public ZetProcess Zet => _zet ?? throw new InvalidOperationException("ziti-edge-tunnel not started");
    public string WorkingSignerId => _workingSignerId ?? throw new InvalidOperationException("working ext-jwt signer not created");

    private Quickstart? _quickstart;
    private Dex? _dex;
    private ZetProcess? _zet;
    private string? _workingSignerId;
    private bool _caTrusted;

    public IntegrationFixture() : this("integration")
    {
    }

    protected IntegrationFixture(string homeName)
    {
        Home = Path.Combine(TestHelpers.RepoRoot(), "UITests", "TestResults", homeName);
    }

    /// <summary>The controller ZET runs against, once the fixture is imported.</summary>
    protected virtual Task<Quickstart> ApplyAuthModeAsync(Quickstart quickstart) => Task.FromResult(quickstart);

    public async Task InitializeAsync()
    {
        _quickstart = await Quickstart.StartAsync(RequiredBinary("ZITI_BIN"), Path.Combine(Home, "quickstart"));
        _dex = await Dex.StartAsync(RequiredBinary("IDP_BIN"),
            Path.Combine(AppContext.BaseDirectory, "testdata", "dex-config.yaml"), Path.Combine(Home, "idp"));
        // A CA left by a crashed run goes first, because ziti 1.6's ops import fails against an OS-trusted controller,
        // and this run's CA goes in after the import.
        if (OsCaTrust.IsInstalled()) OsCaTrust.Remove();
        // Recopy when ziti-tunnel-sdk-c's tests/integration/testdata/fixture.json changes.
        _quickstart.ImportFixture(Path.Combine(AppContext.BaseDirectory, "testdata", "fixture.json"));
        // Starts enroll-to-none, and every test that changes it restores that.
        _workingSignerId = _quickstart.CreateExtJwtSigner(
            new Quickstart.ExtJwtSigner(WorkingSignerName, Dex.IssuerUrl, _dex.JwksUri, Dex.ClientIdWorks));
        _quickstart = await ApplyAuthModeAsync(_quickstart);
        _caTrusted = true;
        try
        {
            OsCaTrust.Install(_quickstart.RootCaPath);
            _zet = await StartZetAsync();
        }
        catch
        {
            _caTrusted = false;
            // A failed install may have added nothing, and certutil -delstore fails when nothing matches.
            if (OsCaTrust.IsInstalled()) OsCaTrust.Remove();
            throw;
        }
    }

    /// <summary>Swap in the controller a restart returns. The CA stays trusted, since the restart keeps the same home.</summary>
    protected async Task ReplaceQuickstartAsync(Func<Quickstart, Task<Quickstart>> restart)
    {
        _quickstart = await restart(Quickstart);
    }

    private Task<ZetProcess> StartZetAsync() =>
        ZetProcess.StartAsync(RequiredBinary("ZET_BIN"), ZetDiscriminator, Path.Combine(Home, "zet"));

    /// <summary>Kill ZET and start it again on the same identity dir, which it reloads with its config.json.</summary>
    public async Task RestartZetAsync()
    {
        await StopZetAsync();
        await RestartStoppedZetAsync();
    }

    /// <summary>Kill ZET, leaving its identity dir for RestartStoppedZetAsync.</summary>
    public async Task StopZetAsync()
    {
        Step.Log("stopping ziti-edge-tunnel");
        await Zet.DisposeAsync();
        _zet = null;
    }

    public async Task RestartStoppedZetAsync()
    {
        if (_zet != null) throw new InvalidOperationException("ziti-edge-tunnel is still running");
        Step.Log("starting ziti-edge-tunnel");
        _zet = await StartZetAsync();
    }

    /// <summary>The binary an environment variable names. run-ui-tests.ps1 requires all of them.</summary>
    private static string RequiredBinary(string variable)
    {
        string? path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException($"{variable} is not set. Run the integration tests through UITests\\run-ui-tests.ps1.");
        if (!File.Exists(path)) throw new FileNotFoundException($"{variable}={path} does not exist", path);
        return path;
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_zet != null) await _zet.DisposeAsync();
        }
        finally
        {
            if (_caTrusted) OsCaTrust.Remove();
            try
            {
                if (_dex != null) await _dex.DisposeAsync();
            }
            finally
            {
                if (_quickstart != null) await _quickstart.DisposeAsync();
            }
        }
    }
}
