using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Application.Common;
using MotsSupplierPortal.Domain.Audit;
using MotsSupplierPortal.Infrastructure.Persistence;
using Npgsql;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// FR-AUD-002 / NFR-CMP-002: "no user, including admin, can edit or delete" AuditLog entries.
///
/// <para><b>Why raw ADO.NET rather than EF.</b> EF Core never issues an UPDATE or DELETE against
/// this table - <c>AuditLogger</c> only ever calls <c>Add</c>. Testing through EF would therefore
/// prove the application's convention, which was never in question; it says nothing about whether
/// the database itself would stop a bug, a future migration, or an engineer who does not know the
/// convention from mutating an existing row directly. So these tests open a raw
/// <see cref="NpgsqlConnection"/> on the same connection string and issue SQL by hand, bypassing
/// EF's change tracker entirely - the same access a rogue script or a careless migration would
/// have.</para>
///
/// <para><b>Why this counts as "from a fresh migration."</b> <see cref="PostgresApiFixture"/> spins
/// up a new Postgres Testcontainer and runs <c>Database.MigrateAsync()</c> against it from empty
/// for every test class that uses it - this suite included. There is no hand-applied state; the
/// trigger under test only exists here because the migration created it.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuditLogImmutabilityTests(PostgresApiFixture fixture)
{
    private async Task<(NpgsqlConnection Connection, Guid RowId)> OpenConnectionWithExistingRowAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        // A real row, written the real way - through the application's own audit path - rather
        // than inserted by the test. What is under test is whether an EXISTING row can be
        // mutated, not whether a hand-crafted one can.
        var rowId = Guid.CreateVersion7();
        await db.AuditLogs.AddAsync(new AuditLog
        {
            Id = rowId,
            OccurredAt = DateTimeOffset.UtcNow,
            ActorKind = AuditActorKind.System,
            AggregateType = "AuditLogImmutabilityTests",
            AggregateId = rowId,
            Action = "seeded_for_immutability_test",
        });
        await db.SaveChangesAsync();

        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        return (connection, rowId);
    }

    [Fact]
    public async Task A_direct_UPDATE_against_an_existing_row_is_rejected()
    {
        var (connection, rowId) = await OpenConnectionWithExistingRowAsync();
        await using var _ = connection;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """UPDATE ops.audit_log SET "Action" = 'tampered' WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", rowId);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>(
            "the database itself, not application convention, must refuse to rewrite an audit row");
        ex.Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task A_direct_DELETE_against_an_existing_row_is_rejected()
    {
        var (connection, rowId) = await OpenConnectionWithExistingRowAsync();
        await using var _ = connection;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """DELETE FROM ops.audit_log WHERE "Id" = @id""";
        cmd.Parameters.AddWithValue("id", rowId);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>(
            "erasing history is the more serious of the two mutations the trigger exists to stop");
        ex.Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task The_trigger_blocks_UPDATE_and_DELETE_but_not_INSERT()
    {
        // The negative-space proof. A trigger firing on every write - not only UPDATE/DELETE -
        // would silently break every audited action in the system, and that failure mode would
        // not look like this ticket; it would look like every OTHER integration test failing for
        // an unrelated-looking reason. Asserted directly rather than inferred from "the rest of
        // the suite still passes."
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rowId = Guid.CreateVersion7();
        var act = async () =>
        {
            await db.AuditLogs.AddAsync(new AuditLog
            {
                Id = rowId,
                OccurredAt = DateTimeOffset.UtcNow,
                ActorKind = AuditActorKind.System,
                AggregateType = "AuditLogImmutabilityTests",
                AggregateId = rowId,
                Action = "insert_must_still_work",
            });
            await db.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();

        (await db.AuditLogs.CountAsync(a => a.Id == rowId)).Should().Be(1);
    }
}
