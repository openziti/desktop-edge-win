using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// xUnit class-fixture that launches one UI process against the default
/// landing-status.json fixture and shares the Appium session across every
/// test in the consuming class. Cuts per-test launch overhead from ~3s to ~0.3s
/// for read-only assertions.
/// </summary>
public sealed class LandingSession : IAsyncLifetime
{
    public AppiumSession Session { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Session = await AppiumSession.LaunchAsync(
            TestHelpers.DefaultExePath(), TestHelpers.FixturesDir());
        TestHelpers.WaitForId(Session, "ConnectLabel");
    }

    public async Task DisposeAsync()
    {
        await Session.DisposeAsync();
    }
}
