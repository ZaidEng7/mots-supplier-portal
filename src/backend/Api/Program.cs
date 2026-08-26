using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Reference;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: structured JSON logging (docs/architecture/OBSERVABILITY-ARCHITECTURE.md)
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "MotsSupplierPortal.Api")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// OpenTelemetry: traces with correlationId propagation
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MotsSupplierPortal.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=mots_supplier_portal;Username=postgres;Password=postgres"));

builder.Services.AddScoped<IGetCurrenciesHandler, GetCurrenciesHandler>();

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=mots_supplier_portal;Username=postgres;Password=postgres");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();

// Walking-skeleton slice (docs/backlog/ROADMAP.md Phase 0):
// health + a real reference-data read through every layer.
app.MapHealthChecks("/health");

app.MapGet("/api/v1/reference/currencies", async (IGetCurrenciesHandler handler, CancellationToken ct) =>
    {
        var currencies = await handler.HandleAsync(ct);
        return Results.Ok(currencies);
    })
    .WithName("GetCurrencies")
    .WithTags("Reference");

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
