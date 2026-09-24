// Every route this API serves, mapped in one place.
//
// Most of it is one line per feature, each calling into the endpoint file that owns those routes. What
// is written out here is the handful of routes that belong to no feature: the build information, the
// two health probes, the metrics scrape, the public reference lists, and the published contract
// document.
//
//
// THE CONTRACT DOCUMENT
//
// It is published in every environment, because a contract that exists only where the code is being
// written cannot be compared against what is deployed.
//
// In local development it is open. Anywhere else it requires the administrator permission, matching the
// rule stated for the interactive viewer. The document lists every route and its shapes, which is not a
// secret, but it is a map, and a map is worth asking for a name first.
//
// The open version is a fix rather than a relaxation: the deny-by-default rule applies to every route
// that says nothing, and this one said nothing, so it answered a refusal from the day that rule landed.
// The document was being generated and served to nobody, while three things depended on fetching it.
// Found by fetching it.
//
// The permission is attached directly rather than through the usual extension method, because this
// mapping returns a different builder type than that method accepts. Same filter, same permission, just
// reached without the sugar.
//
//
// BUILD INFORMATION
//
// Anonymous, deliberately. An about screen has to render for somebody who cannot sign in, which is
// exactly when knowing the build matters, and a build number names software rather than data.
//
// Nothing environment-specific is emitted: no connection strings, no host names, no feature switches,
// because a public route that grows those becomes reconnaissance.
//
// The version comes from the build's own version string, which the build pipeline stamps with the
// commit, so there is nothing here for a release to forget to update. It is split rather than parsed,
// because a local run has no commit in it and reporting the whole string as a version number would be
// wrong in the one case a developer reads this most. A missing commit is reported as nothing rather than
// as the word unknown, so a caller can tell the two apart.
//
// The maintenance notice rides on this route rather than getting one of its own, because the interface
// needs it in exactly the same circumstances: anonymously, on any page, including the sign-in screen an
// outage would otherwise strand people on. An operator sets the wording and optionally a window; absent
// configuration means no banner, so a fresh deployment says nothing rather than showing a placeholder
// nobody wrote.
//
// The window is echoed verbatim and nothing here decides whether it has started. The operator who set
// the message is the one who knows, and a server that hid its own notice because a clock disagreed would
// be worse than one that shows it.
//
//
// THE HEALTH PROBES
//
// Both answer without signing in, or they are useless to an orchestrator.
//
// Liveness matches no checks at all, so it confirms only that the process is up and the pipeline can
// run. Readiness matches the ones tagged for it and reports each by name, because an operator reading
// this should learn which dependency is down rather than only that something is.
//
// They share one response writer, so the empty list from liveness is verifiable in the same shape as
// readiness's four rather than being a differently formatted success.
//
//
// THE METRICS SCRAPE
//
// Metrics in the text format any collector understands, rather than a dashboard of our own, which would
// be a much larger undertaking and not what the requirement needs. It is open for the same reason the
// health probes are: a scrape endpoint is conventionally reachable from an internal collector network,
// and firewalling it belongs to the deployment rather than to this application's own permission system.
//
//
// THE PUBLIC REFERENCE LISTS
//
// Deliberately open, because the registration form is itself unauthenticated and needs regions and
// currencies to render. They were previously open only because no guard had been attached; now it is a
// stated decision. The contents are non-sensitive seed lists with no supplier or personal data in them.
//
// The delivery terms are open like the rest, because a bidder reads the terms on offer while deciding
// whether to register at all.
//
//
// THE BACKGROUND-JOBS DASHBOARD
//
// Mapped in local development only, and it requires a system administrator rather than merely a
// signed-in user.
//
// Previously the only gate was the deny-anonymous floor, which closed anonymous access and nothing
// more, so any supplier's own administrator could read every job's arguments, including other
// suppliers' email addresses and live verification and reset links.
//
// Mapping it in production is a separate exposure decision and is not taken here, so the requirement
// that job health be observable to administrators is still unmet there. The authorisation shape is
// fixed first, precisely because the production dashboard, when it is built, will be built on this one.
//
//
// THE DEVELOPMENT-ONLY SEEDING
//
// The deliberate-failure route exists to prove a negative. The contract says a server error never
// includes a stack trace, database text or an internal message, and that can only be proven by taking
// the path on purpose, so a test plants a recognisable secret here and asserts it does not reach the
// response. The static analyser flags the fake credential in it; removing the credential would delete
// the evidence the assertion depends on, so it is suppressed at the line rather than project-wide, and
// a real hardcoded credential elsewhere still fails the build.
//
// The first administrator account is seeded outside the demo-data switch, because somebody has to
// create the first staff accounts and registration only ever produces a supplier. Its second-factor
// secret is printed once, on the run that creates the account, because the account survives later runs
// and the secret is only ever generated once.
//
// Everything else is behind a switch, and that was a defect before it was a switch. The integration
// tests run the host as local development, because they need its relaxed settings, so this seeder ran
// inside the test host too, and its suppliers turned up in the middle of assertions that name the exact
// rows they expect on the first page. Found in a full-suite run, and every one of those tests would
// have kept passing on its own, which is the worst shape a fixture defect can take.
//
// The reviewer account moved inside that switch later. It was outside, which meant turning demo data
// off still left one demo account standing, and no demo data that leaves an account behind is a switch
// anybody can trust.
//
// It is a configuration switch rather than an environment check because the test host already overrides
// settings, and nothing else has to know why.

namespace MotsSupplierPortal.Api.Startup;

using System.Reflection;
using Hangfire;
using Microsoft.AspNetCore.Identity;
using MotsSupplierPortal.Api.Endpoints;
using MotsSupplierPortal.Application.ReferenceData;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class ApiEndpointRegistration
{
    internal static WebApplication MapApiContractDocument(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
        }
        else
        {
            app.MapOpenApi()
                .AddEndpointFilter(new MotsSupplierPortal.Api.Authorization.PermissionEndpointFilter(
                    MotsSupplierPortal.Domain.Identity.Permissions.AdminUsersManage))
                .RequireAuthorization();
        }

        return app;
    }

    internal static async Task<WebApplication> SeedDevelopmentDataAsync(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {

        #pragma warning disable S2068 // Hard-coded credentials are security-sensitive
            app.MapGet("/__test/throw", (Func<IResult>)(() =>
                throw new InvalidOperationException(
                    "LEAK_CANARY_a7f3d2e1: connection string Host=db;Password=hunter2; at Table supplier.legal_info")))
        #pragma warning restore S2068
                .AllowAnonymous()
                .WithName("TestThrow");

            using var scope = app.Services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            await RoleSeeder.SeedAsync(roleManager);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.SeedAsync(userManager, app.Configuration);
            if (MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.TotpSecret is { } totpSecret)
            {
                Console.WriteLine($"[dev-seed] system_admin created: {MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.Email} / {MotsSupplierPortal.Infrastructure.Identity.AdminSeeder.PasswordUsed}");
                Console.WriteLine($"[dev-seed] TOTP secret (add to an authenticator app): {totpSecret}");
            }

            if (app.Configuration.GetValue("DevSeed:Enabled", defaultValue: true))
            {
                await MotsSupplierPortal.Infrastructure.Identity.ReviewerSeeder.SeedAsync(userManager, app.Configuration);

                var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await MotsSupplierPortal.Infrastructure.Identity.DevDataSeeder.SeedAsync(seedDb, userManager, app.Configuration, app.Environment);
                Console.WriteLine($"[dev-seed] demo personas: officer@ manager@ evaluator@ ministry@ supplier@ supplier.user@mots.local / {MotsSupplierPortal.Infrastructure.Identity.DevDataSeeder.Password}");
            }
        }

        return app;
    }

    internal static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/meta", () =>
        {
            var assembly = typeof(Program).Assembly;
            var informational = assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            var parts = informational?.Split('+', 2) ?? [];

            var maintenance = app.Configuration.GetSection("Maintenance");
            var messageEn = maintenance["MessageEn"];
            var messageAr = maintenance["MessageAr"];

            return Results.Ok(new
            {
                version = parts.Length > 0 ? parts[0] : assembly.GetName().Version?.ToString(),
                commit = parts.Length > 1 ? parts[1] : null,
                maintenance = string.IsNullOrWhiteSpace(messageEn) && string.IsNullOrWhiteSpace(messageAr)
                    ? null
                    : new
                    {
                        messageAr,
                        messageEn,
                        from = maintenance["From"],
                        to = maintenance["To"],
                    },
            });
        })
        .AllowAnonymous()
        .WithName("Meta");

        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponse,
        }).AllowAnonymous();

        app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

        app.MapGet("/api/v1/reference/currencies", async (IGetCurrenciesHandler handler, CancellationToken ct) =>
            {
                var currencies = await handler.HandleAsync(ct);
                return Results.Ok(currencies);
            })
            .AllowAnonymous()
            .WithName("GetCurrencies")
            .WithTags("Reference");

        app.MapGet("/api/v1/reference/regions", async (IGetRegionsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .AllowAnonymous()
            .WithName("GetRegions")
            .WithTags("Reference");

        app.MapGet("/api/v1/reference/categories", async (IGetCategoriesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .AllowAnonymous()
            .WithName("GetCategories")
            .WithTags("Reference");

        app.MapGet("/api/v1/reference/units-of-measure", async (IGetUnitsOfMeasureHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .AllowAnonymous()
            .WithName("GetUnitsOfMeasure")
            .WithTags("Reference");

        app.MapGet("/api/v1/reference/incoterms", async (IGetIncotermsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
            .AllowAnonymous()
            .WithName("GetIncoterms")
            .WithTags("Reference");

        app.MapAdminOverviewEndpoints();
        app.MapOperationsEndpoints();
        app.MapUiStringEndpoints();
        app.MapSearchEndpoints();
        app.MapEmailTemplateEndpoints();
        app.MapSystemSettingEndpoints();
        app.MapNotificationTemplateEndpoints();
        app.MapGovernanceEndpoints();
        app.MapReferenceDataAdminEndpoints();
        app.MapDashboardEndpoints();
        app.MapNotificationEndpoints();
        app.MapRegistrationEndpoints();
        app.MapAuthEndpoints();
        app.MapMfaEndpoints();
        app.MapSupplierEndpoints();
        app.MapSupplierUserEndpoints();
        app.MapAuditEndpoints();
        app.MapAdminEndpoints();
        app.MapDocumentEndpoints();
        app.MapReviewEndpoints();
        app.MapSupplierDirectoryEndpoints();
        app.MapSupplierExportEndpoints();
        app.MapMinistryFeedEndpoints();
        app.MapMapTileEndpoints();
        app.MapOrganizationEndpoints();
        app.MapApiKeyEndpoints();
        app.MapStaffEndpoints();
        app.MapRoleEndpoints();
        app.MapOfferingEndpoints();
        app.MapEvaluationTemplateEndpoints();
        app.MapRfqEndpoints();
        app.MapProposalEndpoints();
        app.MapEvaluationEndpoints();
        app.MapComparisonEndpoints();
        app.MapReportEndpoints();
        app.MapAwardEndpoints();
        app.MapWorkspaceEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapHangfireDashboard("/hangfire", new DashboardOptions
            {
                Authorization = [new MotsSupplierPortal.Api.Authorization.HangfireDashboardAuthorization()],
            });
        }

        return app;
    }

    private static async Task WriteHealthResponse(
        HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
        };
        await context.Response.WriteAsJsonAsync(payload, CancellationToken.None);
    }
}
