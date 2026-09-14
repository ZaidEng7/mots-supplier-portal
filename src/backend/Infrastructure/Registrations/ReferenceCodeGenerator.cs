// Allocating the public reference codes, in a type-year-sequence shape.
//
// Internal keys are time-ordered identifiers and are never exposed in a URL. These codes are what a person quotes.
//
//
// A COUNT IS NOT A SEQUENCE, AND THIS USED TO USE ONE
//
// The sequence was derived from a count of existing rows, which had two independent failure modes, both observed.
//
// A deletion gap: removing any row makes the count fall below the highest code already issued, so the next
// registration re-issues a code that exists. The cleanup job runs daily, so this is reachable in ordinary
// operation, and on the development database it had already happened. Registration failed every time and would
// stay broken until the count climbed back.
//
// And a race: two concurrent registrations both read the same count and both write one more than it.
//
// Allocation is now a single database statement that locks, increments and reads the row within itself, so two
// concurrent callers are serialised by the database and cannot observe the same value. Anything that read the
// counter and wrote it back from application code would merely move the race rather than remove it.
//
// The counter is only ever incremented and is never recomputed from the rows that currently exist, so deleting
// suppliers can never cause reuse. A new year simply inserts its own row, so the rollover needs no branch and no
// scheduled task, and a new letter prefix needs no schema change either.
//
//
// IT RUNS ON ITS OWN CONNECTION, AND THE COST OF THAT IS GAPS
//
// Registration allocates inside a transaction that also creates the sign-in account, which means a deliberately
// slow password hash. Sharing the caller's connection would hold this row's lock for the whole of that
// transaction, serialising every concurrent registration in the system behind that hash.
//
// On a separate connection the statement commits immediately and the lock is released at once.
//
// The cost is that a value stays consumed if the caller's transaction later rolls back. That is the correct trade
// and matches how the database's own sequences behave, which do not roll back either. Gaps are harmless; reuse is
// not.

namespace MotsSupplierPortal.Infrastructure.Registrations;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using MotsSupplierPortal.Infrastructure.Persistence;

public static class ReferenceCodeGenerator
{
    public static async Task<string> NextSupplierCodeAsync(AppDbContext db, CancellationToken ct) =>
        await NextCodeAsync(db, "SUP", ct);

    public static async Task<string> NextCodeAsync(AppDbContext db, string typePrefix, CancellationToken ct)
    {
        var prefix = $"{typePrefix}-{DateTime.UtcNow.Year}-";
        var next = await NextValueAsync(db, prefix, ct);
        return $"{prefix}{next:D6}";
    }

    private static async Task<long> NextValueAsync(AppDbContext db, string prefix, CancellationToken ct)
    {
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
