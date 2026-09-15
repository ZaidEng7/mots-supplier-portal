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

namespace MotsSupplierPortal.Api.Startup;

using Hangfire;
using Hangfire.PostgreSql;
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

        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                c => c.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = hangfireSchema,
                    PrepareSchemaIfNecessary = prepareHangfireSchema,
                }));

        builder.Services.AddHangfireServer();

        return builder;
    }
}
