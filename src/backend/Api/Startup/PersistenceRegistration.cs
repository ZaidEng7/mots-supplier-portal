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

        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                c => c.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = hangfireSchema }));

        builder.Services.AddHangfireServer();

        return builder;
    }
}
