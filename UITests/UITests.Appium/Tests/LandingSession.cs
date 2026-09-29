using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// One UI process on landing-status.json shared by every test in the class, for read-only assertions: a launch
/// costs about 3s, a shared test about 0.3s.
/// </summary>
public sealed class LandingSession : IAsyncLifetime
{
    public AppiumSession Session { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Session = await AppiumSession.LaunchAsync(
            TestHelpers.DefaultExePath(), TestHelpers.Fixture("landing-status.json"));
        TestHelpers.WaitForId(Session, "ConnectLabel");
        await TestHelpers.PrepareTestWindow(Session);
    }

    public async Task DisposeAsync()
    {
        await Session.DisposeAsync();
    }
}
