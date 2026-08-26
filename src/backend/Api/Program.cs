using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

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
builder.Services.AddScoped<IGetSupplierHandler, GetSupplierHandler>();
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
        .AllowAnyMethod());
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
app.MapSupplierEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program; // exposed for WebApplicationFactory integration tests
