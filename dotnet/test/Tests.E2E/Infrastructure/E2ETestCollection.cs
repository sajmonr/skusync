namespace Tests.E2E.Infrastructure;

/// <summary>
/// Shares a single <see cref="AppServerTestHost"/> across all test classes in the
/// "E2E" collection. xUnit serializes tests within a collection, which is required because
/// the factory injects configuration via process-wide environment variables; running two
/// factories in parallel would race and corrupt each other's connection strings.
/// </summary>
[CollectionDefinition(Name)]
public class E2ETestCollection : ICollectionFixture<AppServerTestHost>
{
    public const string Name = "E2E";
}
