// Who can sign in, how a token is proved, and the rule that applies to a route which declares nothing.
//
//
// PASSWORDS
//
// Twelve characters minimum with no forced mixture of cases, digits or symbols. Length over
// composition, following the current guidance, so a passphrase is not punished in favour of
// predictable patterns. It used to be ten characters with forced composition, which is backwards from
// the documented design.
//
// Five failed attempts locks the account for fifteen minutes.
//
// A confirmed email address is not required to sign in here, because that is enforced after signing in,
// where the answer can say which of several reasons an account is not yet usable.
//
// Breached-password checking is a password validator, so it applies wherever a password is set rather
// than only where somebody remembered to call it.
//
//
// WHY PASSWORD-RESET TOKENS HAVE THEIR OWN LIFESPAN
//
// Thirty minutes, separate from the default provider's twenty-four hours, which is kept deliberately
// for email-confirmation links.
//
// A security review found the default provider reused for both, which left reset links valid
// forty-eight times longer than the documented design.
//
//
// TOKENS
//
// Signed with an asymmetric key rather than a shared secret, so a worker or another service can verify
// a token holding only the public half.
//
// The key provider is built directly rather than resolved from the container, because the
// authentication options need the validation key at registration time, before the container exists. It
// is registered as a singleton afterwards so the signing side shares the exact same key.
//
// Inbound claim names are kept verbatim rather than remapped to the framework's long default names,
// because the request context reads the short ones directly.
//
// The clock skew is read from configuration rather than left as a literal, because one of the admin
// screens has to report it: the skew is why a fifteen-minute token is not one, and a screen restating
// the number would drift the first time somebody changed this line. One value, two readers.
//
//
// DENY BY DEFAULT
//
// With no fallback rule, a route that simply forgot to declare a permission or a sign-in requirement
// was served to anybody. The default is now a refusal, and a public route has to say so out loud, so
// the intent is visible in the code rather than inferred from an omission.
//
// An architecture test enforces that every mapped route declares one or the other, so this cannot
// silently regress.

namespace MotsSupplierPortal.Api.Startup;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using MotsSupplierPortal.Application.Auth;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Auth;
using MotsSupplierPortal.Infrastructure.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class AccessRegistration
{
    private const string PasswordResetTokenProviderName = "PasswordReset";

    internal static WebApplicationBuilder AddAccessControl(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DataProtectionTokenProviderOptions>(PasswordResetTokenProviderName, options =>
            options.TokenLifespan = TimeSpan.FromMinutes(30));

        builder.Services
            .AddIdentityCore<AppUser>(options =>
            {
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
                    ClockSkew = TimeSpan.FromSeconds(builder.Configuration.GetValue("Jwt:ClockSkewSeconds", 30)),
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        builder.Services.AddHttpContextAccessor();

        return builder;
    }
}
