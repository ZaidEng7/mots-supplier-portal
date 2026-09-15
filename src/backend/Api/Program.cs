// Where the application starts. Read top to bottom, this is the whole order of events: what is
// registered, in what order the middleware runs, what routes exist, and what is scheduled.
//
// Each step is one call into a file under Startup, and each of those files explains its own decisions.
// This file deliberately holds no reasoning of its own beyond the order, because the order is the only
// thing that is true of the application as a whole rather than of one concern.
//
// It used to be twelve hundred lines doing all of it inline, including two hundred and thirty-eight
// service registrations, and a reader looking for the middleware order had to find it among them.
//
// Three things stay here because they are genuinely shared.
//
// The connection string is read once and passed to the two steps that need it, the database and the
// health probes, rather than read twice from configuration.
//
// The required-configuration check runs before any service reads a setting, because a misconfigured
// deployment must not start. Its warnings are reported after the application is built, when there is a
// logger to report them with.
//
// The backlog gauge is resolved eagerly, immediately after the build. A singleton nobody resolves is
// never constructed, and its callback is therefore never registered with the metrics system, which
// reads at the metrics endpoint as the gauge simply not being there rather than as any kind of error.

using MotsSupplierPortal.Api.Configuration;
using MotsSupplierPortal.Api.Startup;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Infrastructure.Suppliers;

ObservabilityRegistration.PinInvariantCulture();

var builder = WebApplication.CreateBuilder(args);

RequiredConfiguration.Validate(builder.Configuration, builder.Environment);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=mots_supplier_portal;Username=postgres;Password=postgres";

builder
    .AddObservability()
    .AddApiContract()
    .AddPersistence(connectionString)
    .AddAccessControl()
    .AddApplicationHandlers()
    .AddHealthProbes(connectionString)
    .AddHttpTransport()
    .AddForwardedHeaders();

var app = builder.Build();

app.Services.GetRequiredService<OutboxBacklogGauge>();

foreach (var warning in RequiredConfiguration.Warnings(app.Configuration))
{
    app.Logger.LogWarning("Configuration warning: {Warning}", warning);
}

app.UseApiPipeline();
app.MapApiContractDocument();

await app.SeedDevelopmentDataAsync();

app.MapApiEndpoints();

await using (var storageScope = app.Services.CreateAsyncScope())
{
    var minioStorage = storageScope.ServiceProvider.GetRequiredService<MinioFileStorage>();
    await minioStorage.EnsureBucketExistsAsync(CancellationToken.None);
}

app.ScheduleRecurringJobs();

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
