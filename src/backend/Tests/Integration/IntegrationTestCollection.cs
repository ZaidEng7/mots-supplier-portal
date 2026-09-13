// One fixture, and therefore one set of containers and one host, shared by every integration test class.
//
// Each class used to take its own, so the runner's default per-class parallelism spun up an independent set per
// class.
//
// At two classes that was merely wasteful. At four sets starting simultaneously, tests began failing with refused
// connections against containers that were still coming up or had been reaped, which is a flaky-infrastructure
// failure that looks exactly like a real regression, and that is the worst kind to leave in a pipeline.
//
// Sharing a collection also serialises the classes, which matters here because these tests share one database.

namespace MotsSupplierPortal.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "integration";
}
