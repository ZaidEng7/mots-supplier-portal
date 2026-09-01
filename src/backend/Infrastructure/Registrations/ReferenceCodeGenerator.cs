using Microsoft.EntityFrameworkCore;
using Npgsql;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Infrastructure.Registrations;

/// <summary>
/// Opaque public reference codes in TYPE-YEAR-SEQ form, e.g. SUP-2026-000001
/// (docs/product/ASSUMPTIONS.md ASM-086). Internal PKs are GUIDv7 and never exposed in URLs.
///
/// MSP-81. This previously derived the sequence from COUNT(*) of existing rows:
///
///     var count = await db.Suppliers.CountAsync(s => s.ReferenceCode.StartsWith(prefix), ct);
///     return $"{prefix}{count + 1:D6}";
///
/// A count is not a sequence, and that had two independent failure modes, both observed:
///
///  - Deletion gap. Removing any supplier row makes COUNT(*) fall below the highest code already
///    issued, so the next registration re-issues a code that exists. DraftCleanupJob runs daily, so
///    this is reachable in ordinary operation. On the development database it had already happened:
///    25 rows, highest code SUP-2026-000026, generator producing SUP-2026-000026. Registration
///    failed 100% of the time and would stay broken until the count climbed back.
///  - Race. Two concurrent registrations both read count = N and both write N+1.
///
/// Allocation is now done by the database in a single statement, so there is no read-then-write for
/// a second caller to interleave with. The counter is only ever incremented and is never recomputed
/// from the rows that currently exist, so deleting suppliers can never cause reuse.
/// </summary>
public static class ReferenceCodeGenerator
{
    /// <summary>
    /// Atomically claims the next value for a prefix and returns the formatted code.
    ///
    /// Deliberately runs on its OWN connection rather than the caller's. RegisterSupplierHandler
    /// allocates inside a transaction that also creates the Identity user, and that means a PBKDF2
    /// password hash. Sharing the caller's connection would hold this row's lock for the whole of
    /// that transaction, serialising every concurrent registration in the system behind one
    /// deliberately-slow hash. On a separate connection the statement commits immediately and the
    /// lock is released at once.
    ///
    /// The cost of that choice is gaps: if the caller's transaction later rolls back, the value
    /// stays consumed. That is the correct trade here and matches how a Postgres sequence behaves -
    /// nextval() does not roll back either. Gaps are harmless; reuse is not.
    /// </summary>
    public static async Task<string> NextSupplierCodeAsync(AppDbContext db, CancellationToken ct) =>
        await NextCodeAsync(db, "SUP", ct);

    /// <summary>FEAT-07.1/FR-RFQ-011: same atomic allocator, a different prefix ("RFQ-2026-000123",
    /// DOMAIN-MODEL.md §2.2) - the counter table is keyed by the full prefix-plus-year string
    /// (reference_code_counter.Prefix), so a new letter prefix needs no schema change, just a new
    /// row on first use.</summary>
    public static async Task<string> NextCodeAsync(AppDbContext db, string typePrefix, CancellationToken ct)
    {
        var prefix = $"{typePrefix}-{DateTime.UtcNow.Year}-";
        var next = await NextValueAsync(db, prefix, ct);
        return $"{prefix}{next:D6}";
    }

    private static async Task<long> NextValueAsync(AppDbContext db, string prefix, CancellationToken ct)
    {
        // INSERT ... ON CONFLICT DO UPDATE ... RETURNING is a single atomic statement: the row is
        // locked, incremented and read within it, so two concurrent callers are serialised by
        // Postgres and cannot both observe the same value. This is the whole fix - anything that
        // reads the counter and then writes it back from application code would merely move the
        // race rather than remove it.
        //
        // A new year simply inserts its own row, so rollover needs no branch and no scheduled task.
        const string sql = """
            INSERT INTO supplier.reference_code_counter ("Prefix", "LastValue")
            VALUES (@prefix, 1)
            ON CONFLICT ("Prefix")
            DO UPDATE SET "LastValue" = reference_code_counter."LastValue" + 1
            RETURNING "LastValue";
            """;

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("prefix", prefix);

        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
