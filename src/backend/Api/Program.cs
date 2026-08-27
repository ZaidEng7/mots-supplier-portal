using System.Text;
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
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
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
    .AddTokenProvider<DataProtectorTokenProvider<AppUser>>(PasswordResetTokenProviderName);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"]!)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<IGetCurrenciesHandler, GetCurrenciesHandler>();
builder.Services.AddScoped<IRegisterSupplierHandler, RegisterSupplierHandler>();
builder.Services.AddScoped<IVerifyEmailHandler, VerifyEmailHandler>();
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
builder.Services.AddValidatorsFromAssemblyContaining<RegisterSupplierRequestValidator>();

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
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
                PermitLimit = 10,
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseSerilogRequestLogging();

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

app.MapHealthChecks("/health");

app.MapGet("/api/v1/reference/currencies", async (IGetCurrenciesHandler handler, CancellationToken ct) =>
    {
        var currencies = await handler.HandleAsync(ct);
        return Results.Ok(currencies);
    })
    .WithName("GetCurrencies")
    .WithTags("Reference");

app.MapRegistrationEndpoints();
app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapSupplierEndpoints();
app.MapAuditEndpoints();
app.MapDocumentEndpoints();
app.MapReviewEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire");
}

using (var storageScope = app.Services.CreateScope())
{
    var minioStorage = storageScope.ServiceProvider.GetRequiredService<MinioFileStorage>();
    await minioStorage.EnsureBucketExistsAsync(CancellationToken.None);
}

RecurringJob.AddOrUpdate<DocumentExpiryJob>(
    "document-expiry-lifecycle", job => job.RunAsync(CancellationToken.None), Cron.Daily);

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
