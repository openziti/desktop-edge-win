using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// A quickstart controller and a ZET on ZetDiscriminator, shared by every test in IntegrationCollection. Both run from
/// TestResults\integration, which keeps their logs after the run.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public const string ZetDiscriminator = "zdew-ui-test";

    public string Home { get; } = Path.Combine(TestHelpers.RepoRoot(), "UITests", "TestResults", "integration");
    public Quickstart Quickstart => _quickstart ?? throw new InvalidOperationException("quickstart not started");
    public ZetProcess Zet => _zet ?? throw new InvalidOperationException("ziti-edge-tunnel not started");

    private Quickstart? _quickstart;
    private ZetProcess? _zet;

    public async Task InitializeAsync()
    {
        _quickstart = await Quickstart.StartAsync(RequiredBinary("ZITI_BIN"), Path.Combine(Home, "quickstart"));
        // A byte-for-byte copy of ziti-tunnel-sdk-c tests/integration/testdata/fixture.json, so each twin's identity
        // has the same name and auth policy as in the ZET test it mirrors.
        _quickstart.ImportFixture(Path.Combine(AppContext.BaseDirectory, "testdata", "fixture.json"));
        _zet = await ZetProcess.StartAsync(RequiredBinary("ZET_BIN"), ZetDiscriminator, Path.Combine(Home, "zet"));
    }

    /// <summary>
    /// The binary an environment variable names, the variables ziti-tunnel-sdk-c's run-ci.ps1 takes. run-ui-tests.ps1
    /// requires both.
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
        if (_zet != null) await _zet.DisposeAsync();
        if (_quickstart != null) await _quickstart.DisposeAsync();
    }
}
