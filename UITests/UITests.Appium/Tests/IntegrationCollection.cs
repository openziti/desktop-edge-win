namespace ZitiDesktopEdge.UITests.Tests;

/// <summary>Every integration class joins this collection, so one quickstart and ZET serve the whole run.</summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Integration";
}
