// A host runs its jobs with its own services, whatever other host in the same process was configured after it.
//
// THIS IS THE FIX FOR A RANDOM FAILURE, SO IT IS ASSERTED RATHER THAN DESCRIBED. Hangfire's own registration takes a
// host's job activator and job storage from two process-wide statics, JobActivator.Current and JobStorage.Current,
// and every host that configures Hangfire overwrites both. One process normally holds one host, so nobody notices.
// The test run holds many: the suite's host, the hosts tests derive from it, and the hosts of the classes that build
// their own, running in parallel with it. On one CI run the suite's host and a host of ForwardedClientAddressTests
// started in the same instant, the suite's job server picked up the other host's activator, and that host was
// disposed a second later. From then on every job the suite queued failed with "Cannot access a disposed object" -
// more than a thousand of them - and the upload tests waited thirty seconds for a virus scan that never ran. It
// passed locally because it needs the two starts to overlap. So each host now registers its own activator and its
// own storage, and nothing reads the statics.
//
// The test sets up the losing order deliberately rather than hoping for the race: the first host configures Hangfire,
// a second host configures it after and is disposed, and only then does the first host hand out its activator and
// storage. Before the fix the first host handed out the second's.
//
// THE CONTROL IS THE SECOND HOST'S OWN STORAGE, which names the schema it was given. Without it, the first host's
// storage naming the shared schema would also pass on a second host that never took its setting at all, and then
// nothing here would differ between the hosts.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class HostOwnJobServicesTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_host_keeps_its_own_job_activator_and_storage_when_another_host_is_configured_after_it()
    {
        await using var first = fixture.WithWebHostBuilder(_ => { });
        var firstServices = first.Services;
        firstServices.GetRequiredService<IGlobalConfiguration>();

        string secondStorage;
        await using (var second = fixture.WithWebHostBuilder(builder => builder
            .UseSetting("Hangfire:SchemaName", "another_host")
            .UseSetting("Jobs:EnableRecurring", "false")))
        {
            second.Services.GetRequiredService<IGlobalConfiguration>();
            secondStorage = second.Services.GetRequiredService<JobStorage>().ToString()!;
        }

        secondStorage.Should().Contain("Schema: another_host", "the control: the second host took its own schema");

        var storage = firstServices.GetRequiredService<JobStorage>();
        storage.ToString().Should().Contain("Schema: hangfire",
            "the first host's jobs must be read from its own queue, not the one configured last");

        var activator = firstServices.GetRequiredService<JobActivator>();
        using var connection = storage.GetConnection();
        var context = new JobActivatorContext(
            connection,
            new BackgroundJob("1", Job.FromExpression(() => Console.WriteLine()), DateTime.UtcNow),
            new JobCancellationToken(false));

        var activate = () =>
        {
            using var scope = activator.BeginScope(context);
            scope.Resolve(typeof(AppDbContext));
        };

        activate.Should().NotThrow(
            "a job run by the first host must not be built from the services of a host that has since been disposed");
    }
}
