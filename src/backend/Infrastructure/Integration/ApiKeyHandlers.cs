// Issuing, listing and revoking API keys.
//
// THE PERMISSIONS A KEY GETS ARE FIXED HERE AND NOT CHOSEN BY THE CALLER. One permission,
// supplier.registry.export, which is what the ministry's dashboard needs and the whole of it. A create call that
// took a permission list would let the one screen that mints credentials mint any credential, including one
// that can write - and the request body would be the only thing standing between an administrator's mistake and
// a key that approves suppliers. When a second integration needs a different set, that is a deliberate change
// to this line and a test that reads it.
//
// EXPIRY DEFAULTS TO A YEAR and the caller may shorten or lengthen it. The default is the compromise: a key
// that never expires is one nobody rotates, and a key that expires unexpectedly breaks a nightly load at 3am.
// The date is returned and listed so the choice is visible rather than discovered.
//
// THE SECRET IS GENERATED, HASHED, STORED AS A HASH, AND RETURNED ONCE. Nothing here keeps the plaintext beyond
// the response it is written into.
//
// REVOKING TWICE IS NOT AN ERROR. The domain ignores the second call, so a caller who clicks twice, or a script
// that retries, gets the same answer as the first time rather than a failure it has to interpret. Revocation is
// the operation you least want to be fiddly.
//
// A REVOKED KEY IS KEPT AND LISTED rather than deleted. The audit trail names keys by prefix, and a prefix that
// resolves to nothing would make a year-old row unreadable exactly when somebody is asking who read the
// registry.
//
// THE LIST IS NEWEST FIRST, which is the order the screen wants: the key somebody just issued is the one they
// are looking at.

namespace MotsSupplierPortal.Infrastructure.Integration;

using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Application.Integration;
using MotsSupplierPortal.Domain.Integration;
using MotsSupplierPortal.Infrastructure.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class ApiKeyGrant
{
    public static readonly string[] Permissions = [Domain.Identity.Permissions.SupplierRegistryExport];
}

public sealed class CreateApiKeyHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : ICreateApiKeyHandler
{
    public async Task<ApiKeyMutationResult> HandleAsync(CreateApiKeyCommand command, CancellationToken ct)
    {
        var secret = ApiKeySecret.Generate();
        var now = DateTimeOffset.UtcNow;

        var key = new ApiKey
        {
            Id = Guid.CreateVersion7(),
            Name = command.Name.Trim(),
            Prefix = secret.Prefix,
            SecretHash = secret.SecretHash,
            Salt = secret.Salt,
            Permissions = ApiKeyGrant.Permissions,
            CreatedByUserId = scope.UserId ?? Guid.Empty,
            CreatedAt = now,
            ExpiresAt = now.AddDays(command.LifetimeDays ?? ApiKey.DefaultLifetimeDays),
        };

        db.ApiKeys.Add(key);

        await auditLogger.LogAsync(
            aggregateType: "ApiKey",
            aggregateId: key.Id,
            action: "api_key_created",
            actorUserId: scope.UserId,
            referenceCode: key.Prefix,
            reason: key.Name,
            ct: ct);

        await db.SaveChangesAsync(ct);

        return new ApiKeyMutationResult.Created(new CreatedApiKey(ApiKeyMapper.ToSummary(key), secret.Presented));
    }
}

public sealed class ListApiKeysHandler(AppDbContext db) : IListApiKeysHandler
{
    public async Task<IReadOnlyList<ApiKeySummary>> HandleAsync(CancellationToken ct)
    {
        var keys = await db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return [.. keys.Select(ApiKeyMapper.ToSummary)];
    }
}

public sealed class RevokeApiKeyHandler(AppDbContext db, IScopeContext scope, IAuditLogger auditLogger)
    : IRevokeApiKeyHandler
{
    public async Task<ApiKeyMutationResult> HandleAsync(RevokeApiKeyCommand command, CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == command.ApiKeyId, ct);
        if (key is null) return new ApiKeyMutationResult.NotFound();

        var alreadyRevoked = key.RevokedAt is not null;
        key.Revoke(scope.UserId ?? Guid.Empty, DateTimeOffset.UtcNow);

        if (!alreadyRevoked)
        {
            await auditLogger.LogAsync(
                aggregateType: "ApiKey",
                aggregateId: key.Id,
                action: "api_key_revoked",
                actorUserId: scope.UserId,
                referenceCode: key.Prefix,
                reason: key.Name,
                ct: ct);
        }

        await db.SaveChangesAsync(ct);

        return new ApiKeyMutationResult.Revoked(ApiKeyMapper.ToSummary(key));
    }
}

internal static class ApiKeyMapper
{
    internal static ApiKeySummary ToSummary(ApiKey key) => new(
        key.Id,
        key.Name,
        key.Prefix,
        key.Permissions,
        key.CreatedAt,
        key.ExpiresAt,
        key.LastUsedAt,
        key.RevokedAt);
}
