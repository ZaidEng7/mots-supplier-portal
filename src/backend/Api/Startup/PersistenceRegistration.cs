// The database and the durable background-job storage, both on the same connection.
//
// The write-precondition interceptor is resolved per request so it can see that request's own
// precondition header.
//
// Background jobs are stored in the database rather than in memory, so a queued email survives a
// restart.
//
// Their schema is configuration rather than a constant. A hardcoded schema makes this host's job
// storage and every other host's job storage the same thing, so a test that needs to watch real
// scheduling has no way to do it without writing into the storage every other test shares. It defaults
// to the library's own name, so nothing changes for a deployment that does not set it.
//
// Whether that schema is PREPARED here is configuration for a different reason. Preparing it is DDL,
// and a deployment following ops/sql/app-role.sql connects at runtime as a role that deliberately
// cannot issue DDL, so the preparation has to move to the deploy step where the owner runs migrations.
// It defaults to true, which is what has always happened, so a deployment that does not set it behaves
// exactly as before; a least-privilege deployment sets it false and prepares the schema once as the
// owner. Getting this wrong fails loudly at start-up rather than quietly at the first queued job,
// because Hangfire touches its storage while the host is building.
//
// Whether this host also RUNS jobs is configuration too, and for the test suite's sake. Every host on the same
// storage competes for the same queue, and the integration tests start extra hosts to swap in a fake ERP or a
// probe - each with twenty workers of its own. When a test disposed such a host, some of its workers went on
// taking jobs from the shared queue against a disposed service provider: the job failed, Hangfire scheduled the
// retry forty seconds out, and the upload tests waiting thirty seconds for a virus scan failed at random. A host
// holding a fake ERP adapter could also have run a real sync job with it. It defaults to true, so every
// deployment runs jobs exactly as before; only the suite's extra hosts set it false.
//
// The job storage and the job activator are this host's own registrations, not the library's defaults. Those
// defaults read two process-wide statics, JobStorage.Current and JobActivator.Current, which every host that
// configures Hangfire overwrites. A deployment holds one host, so it never mattered there; the test run holds
// many at once, and a host that read the statics just after another host started picked up that host's services.
// When the other host was disposed, every job this one ran failed against a disposed service provider. Registered
// here, each host's server runs its jobs with its own container and reads its own queue whatever starts beside it.

namespace MotsSupplierPortal.Api.Startup;

using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class PersistenceRegistration
{
    internal static WebApplicationBuilder AddPersistence(this WebApplicationBuilder builder, string connectionString)
    {
        builder.Services.AddScoped<ExpectedVersionInterceptor>();

        builder.Services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(sp.GetRequiredService<ExpectedVersionInterceptor>()));

        var hangfireSchema = builder.Configuration.GetValue("Hangfire:SchemaName", defaultValue: "hangfire")!;
        var prepareHangfireSchema = builder.Configuration.GetValue("Hangfire:PrepareSchema", defaultValue: true);

        var hangfireStorageOptions = new PostgreSqlStorageOptions
        {
            SchemaName = hangfireSchema,
            PrepareSchemaIfNecessary = prepareHangfireSchema,
        };

        builder.Services.AddSingleton<JobStorage>(_ => new PostgreSqlStorage(
            new NpgsqlConnectionFactory(connectionString, hangfireStorageOptions), hangfireStorageOptions));
        builder.Services.AddSingleton<JobActivator>(sp =>
            new AspNetCoreJobActivator(sp.GetRequiredService<IServiceScopeFactory>()));

        builder.Services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(sp.GetRequiredService<JobStorage>()));

        if (builder.Configuration.GetValue("Hangfire:RunServer", defaultValue: true))
        {
            builder.Services.AddHangfireServer();
        }

        return builder;
    }
}
