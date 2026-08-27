namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// Shares ONE PostgresApiFixture (and therefore one Postgres + one MinIO container, and one API
/// host) across every integration test class.
///
/// Previously each class used IClassFixture, so xUnit's default per-class parallelism spun up an
/// independent container pair per class. At two classes that was merely wasteful; adding the
/// MSP-65 and MSP-67 suites took it to four pairs starting simultaneously and tests began failing
/// with "Connection refused" against containers that were still coming up or had been reaped -
/// a flaky-infrastructure failure that looks exactly like a real regression, which is the worst
/// kind to leave in a pipeline.
///
/// Collection fixtures also serialize the classes, which matters here because these tests share
/// one database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "integration";
}
