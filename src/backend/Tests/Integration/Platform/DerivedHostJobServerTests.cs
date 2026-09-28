// Only the suite's own host runs background jobs; a host a test derives from it runs none.
//
// THIS IS THE FIX FOR A RANDOM FAILURE, SO IT IS ASSERTED RATHER THAN DESCRIBED. Derived hosts each used to start a
// job server on the storage every host shares. When a test disposed one, some of its workers kept taking jobs from
// the shared queue against a disposed service provider; the job failed, its retry was scheduled forty seconds out,
// and the upload tests - which wait thirty seconds for a virus scan - failed on main and on unrelated pull requests.
// If a derived host ever runs a job server again, this fails instead of those tests failing one run in three.
//
// THE CONTROL IS THE SUITE'S OWN HOST, which must still run one. Without it every job the suite queues would sit in
// the queue forever, and a test asserting "no server anywhere" would pass on exactly that broken state.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class DerivedHostJobServerTests(PostgresApiFixture fixture)
{
    private static bool RunsJobServer(IServiceProvider services) =>
        services.GetServices<IHostedService>().Any(s => s.GetType().Name.Contains("BackgroundJobServer"));

    [Fact]
    public async Task A_derived_host_shares_the_job_storage_but_runs_no_job_server()
    {
        await using var derived = fixture.WithWebHostBuilder(_ => { });

        RunsJobServer(derived.Services).Should().BeFalse(
            "a disposed derived host's workers kept taking jobs from the shared queue and failed them");
    }

    [Fact]
    public void The_suites_own_host_still_runs_the_jobs()
    {
        RunsJobServer(fixture.Services).Should().BeTrue(
            "the control: with no server anywhere every queued job would wait forever");
    }
}
