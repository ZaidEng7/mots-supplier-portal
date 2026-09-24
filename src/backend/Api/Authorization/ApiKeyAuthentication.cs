// Signing in as another system: the authentication scheme behind the ministry's feed pulls.
//
// IT PRODUCES A PRINCIPAL AND NOTHING ELSE, which is why no endpoint in this product had to change to accept
// it. Authorization here is a filter that reads "perms" claims off the caller and asks nothing about how they
// arrived (PermissionEndpointFilter). So a scheme that mints those claims from a key row inherits the whole
// permission model exactly as it stands, and a key is refused everywhere its permissions do not reach without
// one route naming it.
//
// THE HEADER IS Authorization: ApiKey <secret>, beside the bearer scheme rather than a header of our own. A
// caller sending a key where a token belongs, or the reverse, gets a clean 401 from the scheme it named instead
// of a confusing one from the scheme it did not.
//
// A FAILED KEY IS NoResult RATHER THAN Fail. Fail on an optional scheme turns every anonymous request into an
// error, and the difference matters where two schemes are allowed: bearer must still get its chance to
// authenticate a request whose key was absent or wrong.
//
// EVERY REFUSAL IS THE SAME REFUSAL from the caller's side - unknown prefix, wrong secret, expired, revoked,
// permissionless - so a caller learns whether their key works and nothing about why it does not. The log line
// carries the reason, named by prefix, because the person debugging it at 3am is on our side of the wall.
//
// THE KEY IS LOOKED UP BY PREFIX, then verified in fixed time. Looking up by secret would mean either storing
// the secret or hashing the candidate against every row in the table.
//
// LASTUSEDAT IS WRITTEN ON EVERY ACCEPTED REQUEST, in its own save, before the endpoint runs. That is one extra
// write per feed pull and it buys the only answer to "is this credential still in use", which is the question
// that decides whether an unaccounted-for key can be revoked. It is deliberately not part of the caller's
// transaction: a feed read has none, and a stamp that vanished when a request failed would under-report use.
//
// A REVOKED KEY STOPS WORKING ON THE NEXT REQUEST, because this reads the row every time rather than caching.
// Caching would be faster and would mean a revoked credential kept working for the length of the cache, which
// is the one property a revocation must not have.

namespace MotsSupplierPortal.Api.Authorization;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Integration;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string KeyPrefixClaim = "integrationKey";
    public const string PolicyName = "FeedCaller";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith($"{ApiKeyAuthentication.Scheme} ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = header[(ApiKeyAuthentication.Scheme.Length + 1)..].Trim();
        if (!ApiKeySecret.TryParse(presented, out var prefix, out var secret))
        {
            Logger.LogWarning("An API key was presented in a shape this product does not issue.");
            return AuthenticateResult.NoResult();
        }

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix);
        if (key is null)
        {
            Logger.LogWarning("API key {Prefix} is not a key this product has issued.", prefix);
            return AuthenticateResult.NoResult();
        }

        if (!ApiKeySecret.Verify(secret, key.Salt, key.SecretHash))
        {
            Logger.LogWarning("API key {Prefix} was presented with the wrong secret.", prefix);
            return AuthenticateResult.NoResult();
        }

        var now = DateTimeOffset.UtcNow;
        if (!key.IsUsable(now))
        {
            Logger.LogWarning(
                "API key {Prefix} is no longer usable: revoked {Revoked}, expires {Expires}.",
                prefix, key.RevokedAt, key.ExpiresAt);
            return AuthenticateResult.NoResult();
        }

        key.RecordUse(now);
        await db.SaveChangesAsync();

        List<Claim> claims =
        [
            new(ApiKeyAuthentication.KeyPrefixClaim, key.Prefix),
            new(ClaimTypes.Name, key.Name),
        ];
        claims.AddRange(key.Permissions.Select(p => new Claim("perms", p)));

        var identity = new ClaimsIdentity(claims, ApiKeyAuthentication.Scheme);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthentication.Scheme));
    }
}
