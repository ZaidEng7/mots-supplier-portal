// Making every operation that rotates or ends a person's sessions take its turn, and revoking without overwriting.
//
//
// WHY A LOCK AT ALL
//
// A refresh reads its token, retires it and issues the successor. Signing out, revoking a session, resetting or
// changing a password, disabling an account and the staff changes that end sessions read the live tokens and retire
// them. Each is a read followed by a write, and under the database's default isolation two of them interleave. A
// sign-out that read the family before a refresh committed never saw the successor that refresh issued, so the
// session outlived the sign-out recorded against it. A refresh that read its token before a sign-out committed went
// on to issue a successor after it. Two refreshes of one token both rotated it and forked the family into two live
// sessions. Every one of those answered success.
//
// A conditional update alone, the shape SecurityTokenService uses to consume a link once, does not close this. It
// stops two writers retiring the same row twice, but a sign-out's update cannot see a successor that a refresh has
// inserted and not yet committed, so it retires nothing and the successor survives.
//
//
// HOW
//
// Each of those operations opens a transaction here and, as its first statement, takes a Postgres advisory lock that
// belongs to the person whose sessions it touches. A second operation on the same person waits until the first
// commits. Every statement under the default isolation reads what had committed when it started, so whatever the
// second one reads after the lock already includes the first one's work: a sign-out that waited sees the successor,
// and a refresh that waited sees its token retired and is refused. The lock belongs to the transaction, so commit or
// rollback releases it, and a connection that dies cannot leave it held.
//
// It is keyed on the person rather than on the session. Half of these operations end every session a person has,
// and keyed on the session they would need one lock per session, taken in a fixed order. One lock per person covers
// both kinds. The cost is that two of one person's devices refreshing in the same instant take turns, which is a
// matter of milliseconds.
//
// The lock is taken before anything else in the transaction is written. A handler that updated the person's own row
// first and then waited could hold that row while the holder of the lock waited for it, which Postgres ends as a
// deadlock. Several people, which only deactivating a supplier locks, are locked in ascending key order for the same
// reason.
//
//
// THE KEY
//
// Postgres keeps two advisory key spaces that never overlap: a single 64-bit key, which ErpImportLock uses, and a pair
// of 32-bit keys, which this uses, so the two locks can never block each other. The first of the pair names these
// locks. The second is the person's identifier folded to 32 bits from its own bytes rather than from a process's hash
// of it, so every running copy of the API computes the same key. Two people whose identifiers fold alike only take
// turns.
//
//
// REVOKING
//
// RevokeAsync is the one way these handlers write a revocation, and it only fills one that is empty. A token's
// revocation time is what tells a rotated token from a revoked one: the successor is created at the instant its
// predecessor was retired. A later sign-out that overwrote that time made the successor look like it came first, so a
// replay of the old token afterwards read as an ended session instead of a stolen one. It refuses to run outside a
// transaction, because outside one the lock would already have been released.

namespace MotsSupplierPortal.Infrastructure.Auth;

using System.Buffers.Binary;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

internal static class SessionLock
{
    private const int Space = 1_701_207_001;

    public static Task<IDbContextTransaction> BeginAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        BeginAsync(db, [userId], ct);

    public static async Task<IDbContextTransaction> BeginAsync(AppDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var key in userIds.Select(KeyOf).Distinct().Order())
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({Space}, {key})", ct);
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static async Task<int> RevokeAsync(
        AppDbContext db, Expression<Func<RefreshToken, bool>> which, DateTimeOffset at, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Sessions are revoked inside the transaction SessionLock.BeginAsync opens.");
        }

        return await db.RefreshTokens
            .Where(which)
            .Where(t => t.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.RevokedAt, at), ct);
    }

    private static int KeyOf(Guid userId)
    {
        Span<byte> bytes = stackalloc byte[16];
        userId.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }
}
