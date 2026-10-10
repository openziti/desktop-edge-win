using ZitiDesktopEdge.UITests.Drivers;

namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>
/// An IntegrationFixture whose controller advertises no OIDC, so ZET authenticates with legacy api sessions. A legacy
/// controller never tells the client whether an identity has TOTP enrolled.
/// </summary>
public sealed class LegacyAuthFixture : IntegrationFixture
{
    public LegacyAuthFixture() : base("integration-legacy")
    {
    }

    protected override Task<Quickstart> ApplyAuthModeAsync(Quickstart quickstart) => quickstart.RestartWithoutOidcAsync();

    /// <summary>Restart the controller with OIDC, as an admin migrating off legacy auth would. RestoreLegacyAuthAsync undoes it.</summary>
    public Task MigrateToOidcAsync() => ReplaceQuickstartAsync(q => q.RestartWithOidcAsync());

    public Task RestoreLegacyAuthAsync() => ReplaceQuickstartAsync(q => q.RestartWithoutOidcAsync());
}

/// <summary>
/// Classes that need the legacy auth path. Collections run one after another, so this fixture starts only after
/// IntegrationCollection's stopped, on the same ports and ZET pipe.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LegacyAuthCollection : ICollectionFixture<LegacyAuthFixture>
{
    public const string Name = "LegacyAuth";
}
