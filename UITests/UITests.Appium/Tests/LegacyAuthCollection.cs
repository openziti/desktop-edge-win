using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

// A controller without OIDC makes ZET use legacy api sessions, which never say whether TOTP is enrolled.
public sealed class LegacyAuthFixture : IntegrationFixture
{
    public LegacyAuthFixture() : base("integration-legacy")
    {
    }

    protected override Task<Quickstart> StartQuickstartAsync(string zitiExePath, string home) =>
        Quickstart.StartWithoutOidcAsync(zitiExePath, home);

    public Task MigrateToOidcAsync() => ReplaceQuickstartAsync(quickstart => quickstart.RestartWithOidcAsync());

    public Task RestoreLegacyAuthAsync() => ReplaceQuickstartAsync(quickstart => quickstart.RestartWithoutOidcAsync());
}

// Collections run one after another, so this reuses IntegrationCollection's ports and ZET pipe after it stops.
[CollectionDefinition(Name)]
public sealed class LegacyAuthCollection : ICollectionFixture<LegacyAuthFixture>
{
    public const string Name = "LegacyAuth";
}
