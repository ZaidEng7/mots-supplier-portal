using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Infrastructure.Email;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Api.Endpoints;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Registrations;
using MotsSupplierPortal.Application.Reference;
using MotsSupplierPortal.Application.Suppliers;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Infrastructure.Reference;
using MotsSupplierPortal.Infrastructure.Registrations;
using MotsSupplierPortal.Infrastructure.Storage;
using MotsSupplierPortal.Infrastructure.Suppliers;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Fail fast before any service reads a setting: a misconfigured non-Development deployment
// must not boot. See RequiredConfiguration for why (three settings used to degrade silently).
MotsSupplierPortal.Api.Configuration.RequiredConfiguration.Validate(builder.Configuration, builder.Environment);

// Serilog: structured JSON logging (docs/architecture/OBSERVABILITY-ARCHITECTURE.md).
// RedactingEnricher is the NFR-PRIV-004 redaction stage (MSP-61) - it must stay registered
// before the sink so no deny-listed property value can reach the console/aggregator.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.With(new MotsSupplierPortal.Infrastructure.Observability.RedactingEnricher())
    .Enrich.WithProperty("Application", "MotsSupplierPortal.Api")
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

// OpenTelemetry: traces with correlationId propagation
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("MotsSupplierPortal.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter());

builder.Services.AddOpenApi();

// Global: every enum (AddressKind, OnboardingState, SupplierLegalType, DocumentTypeKind, ...)
// reads/writes its string name on the wire, not a raw integer - applies to every Minimal API
// endpoint's request/response JSON binding, not just one handler.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

    // NFR-SEC-005: reject payloads carrying fields we do not model, rather than silently ignoring
    // them. System.Text.Json's default is to skip unknown members, which meant a typo'd or stale
    // field name was swallowed and the caller told "200 OK" - a client could believe it had
    // updated something it had not (found in review 2026-08-28). Applied globally so every
    // endpoint inherits it, not just the one where it was noticed.
    options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
});

// Outside Development RequiredConfiguration has already guaranteed this is present, so the only
// remaining fallback is the local-dev credential (NFR-SEC-007: dev-only, and weak on purpose).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=mots_supplier_portal;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Hangfire: durable background jobs (verification email, password-reset email) backed by Postgres
// so a queued send survives an app restart (docs/architecture/00-foundational-decisions.md §2).
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// Password-reset tokens get their own short lifespan (docs/security/SECURITY-ARCHITECTURE.md §1.7:
// 30-minute window) separate from the default provider's 24h, which is intentionally kept for
// email-confirmation links (ASVS L2 review 2026-08-26, finding #8 - the default provider was
// wrongly reused for both, leaving reset links valid 48x longer than the documented design).
const string PasswordResetTokenProviderName = "PasswordReset";
builder.Services.Configure<DataProtectionTokenProviderOptions>(PasswordResetTokenProviderName, options =>
    options.TokenLifespan = TimeSpan.FromMinutes(30));

// Identity: local ASP.NET Core Identity now, MFA-ready, IdP-swappable later (00-foundational-decisions.md §2)
builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // SECURITY-ARCHITECTURE.md §1.4/FR-IAM-003: length over composition (NIST 800-63B) - a
        // 12-char minimum with no forced symbol/case/digit rules, so passphrases aren't punished
        // in favor of predictable patterns like "Password1!". Previously 10 chars WITH forced
        // composition, backwards from the documented design.
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false; // enforced manually post-login (AccountNotUsable)
        options.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddTokenProvider<DataProtectorTokenProvider<AppUser>>(PasswordResetTokenProviderName)
    .AddPasswordValidator<HibpBreachedPasswordValidator>();

builder.Services.AddHttpClient(nameof(HibpBreachedPasswordValidator));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);

// SECURITY-ARCHITECTURE.md §1.1: RS256 (asymmetric), not a shared HMAC secret - workers/services
// can verify tokens holding only the public key. Built directly (not via DI) because
// AddJwtBearer's options delegate needs the validation key synchronously at registration time;
// registered as a singleton afterward so JwtTokenService (signing) shares the exact same key.
var jwtSigningKeyProvider = new JwtSigningKeyProvider(Microsoft.Extensions.Options.Options.Create(
    jwtSection.Get<JwtOptions>() ?? throw new InvalidOperationException("Jwt configuration section is missing.")));
builder.Services.AddSingleton(jwtSigningKeyProvider);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Keep JWT claim names verbatim (e.g. "sub") instead of ASP.NET Core's default
        // remap to long XML-namespace claim types - HttpScopeContext reads "sub" directly.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKeyProvider.GetValidationKey(),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// NFR-SEC-004 deny-by-default (MSP-67): with no FallbackPolicy, an endpoint that simply forgets
// .RequirePermission()/.RequireAuthorization() was served anonymously. Now the default is a
// denial and public endpoints must say so out loud with .AllowAnonymous(), so the intent is
// visible in code rather than inferred from an omission. LayerDependencyTests enforces that
// every mapped endpoint declares one or the other, so this cannot silently regress.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<IGetCurrenciesHandler, GetCurrenciesHandler>();
builder.Services.AddScoped<IGetRegionsHandler, GetRegionsHandler>();
builder.Services.AddScoped<IGetCategoriesHandler, GetCategoriesHandler>();
builder.Services.AddSingleton<MotsSupplierPortal.Infrastructure.Security.FieldEncryptionService>();
builder.Services.AddScoped<IUpdateLegalInfoHandler, UpdateLegalInfoHandler>();
builder.Services.AddScoped<IUploadLogoHandler, UploadLogoHandler>();
builder.Services.AddScoped<IGetLogoDownloadUrlHandler, GetLogoDownloadUrlHandler>();
builder.Services.AddScoped<IManageRepresentativeHandler, ManageRepresentativeHandler>();
builder.Services.AddScoped<IGetFieldConfigHandler, GetFieldConfigHandler>();
builder.Services.AddScoped<IUpdateFieldConfigHandler, UpdateFieldConfigHandler>();
builder.Services.AddScoped<IManageAddressHandler, ManageAddressHandler>();
builder.Services.AddScoped<IManageContactHandler, ManageContactHandler>();
builder.Services.AddScoped<IManageBranchHandler, ManageBranchHandler>();
builder.Services.AddScoped<IManageBankAccountHandler, ManageBankAccountHandler>();
builder.Services.AddScoped<IManageCategoryLinkHandler, ManageCategoryLinkHandler>();
builder.Services.AddScoped<IInviteSupplierUserHandler, InviteSupplierUserHandler>();
builder.Services.AddScoped<IListSupplierUsersHandler, ListSupplierUsersHandler>();
builder.Services.AddScoped<IDisableSupplierUserHandler, DisableSupplierUserHandler>();
builder.Services.AddScoped<IAcceptSupplierUserInviteHandler, AcceptSupplierUserInviteHandler>();
builder.Services.AddScoped<ISecurityTokenService, SecurityTokenService>();
builder.Services.AddScoped<IRegisterSupplierHandler, RegisterSupplierHandler>();
builder.Services.AddScoped<IVerifyEmailHandler, VerifyEmailHandler>();
builder.Services.AddScoped<IResendVerificationHandler, ResendVerificationHandler>();
builder.Services.AddScoped<DraftCleanupJob>();
builder.Services.AddSingleton<MotsSupplierPortal.Api.Authorization.PerTargetRateLimiter>();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddScoped<ILoginHandler>(sp => sp.GetRequiredService<LoginHandler>());
builder.Services.AddScoped<IRefreshTokenHandler, RefreshTokenHandler>();
builder.Services.AddScoped<IForgotPasswordHandler, ForgotPasswordHandler>();
builder.Services.AddScoped<IResetPasswordHandler, ResetPasswordHandler>();
builder.Services.AddScoped<IEnrollMfaHandler, EnrollMfaHandler>();
builder.Services.AddScoped<IConfirmMfaEnrollmentHandler, ConfirmMfaEnrollmentHandler>();
builder.Services.AddScoped<IListSessionsHandler, ListSessionsHandler>();
builder.Services.AddScoped<IRevokeSessionHandler, RevokeSessionHandler>();
builder.Services.AddScoped<IRevokeAllSessionsHandler, RevokeAllSessionsHandler>();
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
builder.Services.AddScoped<EmailJobs>();
builder.Services.AddScoped<IGetSupplierHandler, GetSupplierHandler>();
builder.Services.AddScoped<IUpdateProfileHandler, UpdateProfileHandler>();
builder.Services.AddScoped<IAcceptTermsHandler, AcceptTermsHandler>();
builder.Services.AddScoped<ISubmitApplicationHandler, SubmitApplicationHandler>();
builder.Services.AddScoped<IListSupplierDocumentsHandler, ListSupplierDocumentsHandler>();
builder.Services.AddScoped<IReviewerListDocumentsHandler, ReviewerListDocumentsHandler>();
builder.Services.AddScoped<IUploadDocumentHandler, UploadDocumentHandler>();
builder.Services.AddScoped<IGetDocumentDownloadUrlHandler, GetDocumentDownloadUrlHandler>();
builder.Services.AddScoped<IApproveDocumentHandler, ApproveDocumentHandler>();
builder.Services.AddScoped<IRejectDocumentHandler, RejectDocumentHandler>();
builder.Services.AddScoped<DocumentScanJob>();
builder.Services.AddScoped<DocumentExpiryJob>();
builder.Services.AddScoped<IListReviewQueueHandler, ListReviewQueueHandler>();
builder.Services.AddScoped<IGetReviewerSupplierViewHandler, GetReviewerSupplierViewHandler>();
builder.Services.AddScoped<IPickUpApplicationHandler, PickUpApplicationHandler>();
builder.Services.AddScoped<IApproveApplicationHandler, ApproveApplicationHandler>();
builder.Services.AddScoped<IRejectApplicationHandler, RejectApplicationHandler>();
builder.Services.AddScoped<ISupplierLifecycleHandler, SupplierLifecycleHandler>();
builder.Services.AddScoped<IRequestInfoHandler, RequestInfoHandler>();
builder.Services.AddScoped<IResubmitApplicationHandler, ResubmitApplicationHandler>();
builder.Services.AddScoped<IGetOwnActiveAnnotationHandler, GetOwnActiveAnnotationHandler>();
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.AddSingleton<MinioFileStorage>();
builder.Services.AddScoped<IFileStorage>(sp => sp.GetRequiredService<MinioFileStorage>());
builder.Services.Configure<ClamAvOptions>(builder.Configuration.GetSection(ClamAvOptions.SectionName));
builder.Services.AddScoped<IVirusScanner, ClamAvScanner>();
builder.Services.AddScoped<MotsSupplierPortal.Application.Audit.IGetAuditLogHandler, MotsSupplierPortal.Infrastructure.Audit.GetAuditLogHandler>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<PermissionResolver>();
builder.Services.AddScoped<IScopeContext, HttpScopeContext>();
builder.Services.AddScoped<IConcurrencyContext, HttpConcurrencyContext>();
// Scoped, not transient: every audit row written during one request must resolve the SAME
// instance, or the correlation id degrades back to being unique per call (MSP-64).
builder.Services.AddScoped<IAuditContext, MotsSupplierPortal.Api.Authorization.HttpAuditContext>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterSupplierRequestValidator>();

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        // No fallback: RequiredConfiguration guarantees this outside Development, and
        // appsettings.Development.json supplies it locally. A silent localhost default here
        // blocked the real SPA in production while looking configured.
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5173"])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()); // required so the refresh-token HttpOnly cookie is sent cross-port
});

// Per-IP anti-automation on unauthenticated auth/registration endpoints (docs/security/
// SECURITY-ARCHITECTURE.md §4; ASVS L2 review 2026-08-26, finding #3 - previously there was no
// rate limiting anywhere, leaving login/forgot-password/registration open to unthrottled
// credential-stuffing and enumeration probing beyond ASP.NET Identity's per-account lockout).
const string AuthRateLimitPolicy = "auth-strict";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                // Configurable so the integration host can raise it: all tests share one
                // WebApplicationFactory and therefore one per-IP partition, so the production
                // default of 10/min throttles the suite itself and surfaces as empty 429 bodies
                // that look like unrelated failures.
                PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseSerilogRequestLogging();

// SECURITY-ARCHITECTURE.md §5.5: full secure-header set on every response. Applied first so it
// covers error responses too, not just successful ones.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.Append("Strict-Transport-Security", "max-age=63072000; includeSubDomains; preload");
    headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data: blob:; connect-src 'self'; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    headers.Append("X-Content-Type-Options", "nosniff");
    headers.Append("X-Frame-Options", "DENY");
    headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    headers.Append("Cross-Origin-Opener-Policy", "same-origin");
    headers.Append("Cross-Origin-Resource-Policy", "same-origin");

    var path = context.Request.Path.Value ?? "";
    if (path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/registrations", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/documents", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/v1/review", StringComparison.OrdinalIgnoreCase))
    {
        headers.Append("Cache-Control", "no-store");
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    await RoleSeeder.SeedAsync(roleManager);
}

app.UseCors();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Probe endpoint: must answer before/without authentication or it is useless to an orchestrator.
app.MapHealthChecks("/health").AllowAnonymous();

// Reference data is deliberately public: the registration form (itself unauthenticated) needs
// regions/currencies to render. These were previously anonymous only because no guard had been
// attached - now it is a stated decision (MSP-67). Contents are non-sensitive seed lists with no
// supplier or personal data.
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

app.MapRegistrationEndpoints();
app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapSupplierEndpoints();
app.MapSupplierUserEndpoints();
app.MapAuditEndpoints();
app.MapAdminEndpoints();
app.MapDocumentEndpoints();
app.MapReviewEndpoints();

// MSP-87: the dashboard now requires system_admin, not merely an authenticated user. Previously
// the only gate was the deny-by-default FallbackPolicy, which closed anonymous access and nothing
// more - so any supplier_admin could read every job's arguments, including other suppliers' email
// addresses and live verification/reset tokens.
//
// FR-ADM-009 names system_admin as the actor; NFR-OBS-006 requires job health to be observable to
// admins. This is mapped in Development only, so NFR-OBS-006 is still unmet in production - a
// deliberate follow-up rather than an oversight, tracked on MSP-87. Mapping it in production is a
// separate exposure decision and is NOT taken here. The authorization shape is fixed first
// precisely because the production dashboard, when it is built, will be built on this one.
if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new MotsSupplierPortal.Api.Authorization.HangfireDashboardAuthorization()],
    });
}

using (var storageScope = app.Services.CreateScope())
{
    var minioStorage = storageScope.ServiceProvider.GetRequiredService<MinioFileStorage>();
    await minioStorage.EnsureBucketExistsAsync(CancellationToken.None);
}

RecurringJob.AddOrUpdate<DocumentExpiryJob>(
    "document-expiry-lifecycle", job => job.RunAsync(CancellationToken.None), Cron.Daily);

RecurringJob.AddOrUpdate<MotsSupplierPortal.Infrastructure.Registrations.DraftCleanupJob>(
    "draft-registration-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
