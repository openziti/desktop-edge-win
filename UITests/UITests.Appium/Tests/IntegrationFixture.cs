using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// A quickstart controller, ZET's dex IdP and a ZET on ZetDiscriminator, shared by every test in IntegrationCollection.
/// All run from TestResults\integration, which keeps their logs after the run.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public const string ZetDiscriminator = "zdew-ui-test";
    // ZET's workingExtJwtSignerName (testutil/common.go).
    public const string WorkingSignerName = "test_ext_auth_signer_working";

    public string Home { get; } = Path.Combine(TestHelpers.RepoRoot(), "UITests", "TestResults", "integration");
    public Quickstart Quickstart => _quickstart ?? throw new InvalidOperationException("quickstart not started");
    public Dex Dex => _dex ?? throw new InvalidOperationException("dex not started");
    public ZetProcess Zet => _zet ?? throw new InvalidOperationException("ziti-edge-tunnel not started");
    public string WorkingSignerId => _workingSignerId ?? throw new InvalidOperationException("working ext-jwt signer not created");

    private Quickstart? _quickstart;
    private Dex? _dex;
    private ZetProcess? _zet;
    private string? _workingSignerId;
    private bool _caTrusted;

    public async Task InitializeAsync()
    {
        _quickstart = await Quickstart.StartAsync(RequiredBinary("ZITI_BIN"), Path.Combine(Home, "quickstart"));
        _dex = await Dex.StartAsync(RequiredBinary("IDP_BIN"),
            Path.Combine(AppContext.BaseDirectory, "testdata", "dex-config.yaml"), Path.Combine(Home, "idp"));
        // The CA trust follows ZET's harness (main_test.go setup): a CA left by a crashed run goes first, because
        // ziti 1.6's ops import fails against an OS-trusted controller, and this run's CA goes in after the import.
        if (OsCaTrust.IsInstalled()) OsCaTrust.Remove();
        // A byte-for-byte copy of ziti-tunnel-sdk-c tests/integration/testdata/fixture.json, so each twin's identity
        // has the same name and auth policy as in the ZET test it mirrors.
        _quickstart.ImportFixture(Path.Combine(AppContext.BaseDirectory, "testdata", "fixture.json"));
        // ZET's SetupWorkingExtJwtSigner. It starts enroll-to-none, and every twin that changes it restores that.
        _workingSignerId = _quickstart.CreateExtJwtSigner(
            new Quickstart.ExtJwtSigner(WorkingSignerName, Dex.IssuerUrl, _dex.JwksUri, Dex.ClientIdWorks));
        _caTrusted = true;
        try
        {
            OsCaTrust.Install(_quickstart.RootCaPath);
            _zet = await ZetProcess.StartAsync(RequiredBinary("ZET_BIN"), ZetDiscriminator, Path.Combine(Home, "zet"));
        }
        catch
        {
            _caTrusted = false;
            // A failed install may have added nothing, and certutil -delstore fails when nothing matches.
            if (OsCaTrust.IsInstalled()) OsCaTrust.Remove();
            throw;
        }
    }

    /// <summary>
    /// The binary an environment variable names, the variables ziti-tunnel-sdk-c's run-ci.ps1 takes. run-ui-tests.ps1
    /// requires all of them.
    /// </summary>
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
