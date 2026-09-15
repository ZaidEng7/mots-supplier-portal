// A backup is a claim until somebody restores it. This restores one.
//
// WHY A TEST AND NOT A RUNBOOK ENTRY
//
// NFR-DR-001 asks for backups with point-in-time recovery, and the honest state of that requirement was
// "documented, never exercised". A recovery-point target nobody has restored against is a plan, and the
// difference only shows on the day it matters. So the drill runs here, on every push, against the real
// schema this product deploys.
//
// WHAT IT PROVES
//
// That a custom-format dump of this database restores into an empty one with the rows intact AND the
// objects a plain row-copy would miss. The audit-log trigger is the sharpest of those: it is the control
// behind FR-AUD-002, it is not expressible as EF model configuration, and a restore that brought back
// every row without it would look completely successful while having silently dropped the one thing
// making the audit log append-only.
//
// WHAT IT DOES NOT PROVE
//
// Not point-in-time recovery. PITR needs continuous WAL archiving to durable storage, which is
// infrastructure rather than a script, and it is what the 15-minute recovery-point target actually
// depends on - a periodic dump gives a recovery point of one interval, not fifteen minutes. Not
// off-site durability, not encryption of the backup at rest, and not the object store holding uploaded
// documents. ops/backup/README.md says which of those are still owed.
//
// IT USES ITS OWN CONTAINER, deliberately. The drill drops and recreates schemas, and the shared fixture
// is one database that every other integration test reads. Borrowing it would make this the test that
// breaks the suite.
//
// pg_dump and pg_restore run INSIDE the container, so the tool version always matches the server and no
// CI runner needs a PostgreSQL client installed.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MotsSupplierPortal.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

public sealed class BackupRestoreDrillTests : IAsyncLifetime
{
    private const string DumpPath = "/tmp/drill.dump";

    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_dump_restores_the_rows_and_the_objects_a_row_copy_would_miss()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        var user = builder.Username!;
        var database = builder.Database!;

        var marker = $"BACKUP-DRILL-{Guid.CreateVersion7()}";
        await ExecuteAsync($"""
            INSERT INTO ops.audit_log
                ("Id", "OccurredAt", "ActorKind", "AggregateType", "AggregateId", "Action", "CorrelationId")
            VALUES ('{Guid.CreateVersion7()}', now(), 'System', 'Drill', '{Guid.CreateVersion7()}',
                    '{marker}', '{Guid.CreateVersion7()}');
            """);

        var dump = await _postgres.ExecAsync(
            ["pg_dump", "-U", user, "-d", database, "--format=custom", "--file", DumpPath]);
        dump.ExitCode.Should().Be(0, "the backup itself must succeed: {0}", dump.Stderr);

        await ExecuteAsync("DROP SCHEMA ops CASCADE;");
        (await ScalarAsync("SELECT to_regclass('ops.audit_log') IS NULL;")).Should().Be(true,
            "the drill has to actually destroy something, or the restore below proves nothing");

        var restore = await _postgres.ExecAsync(
            ["pg_restore", "-U", user, "-d", database, "--clean", "--if-exists", DumpPath]);
        restore.ExitCode.Should().Be(0, "the restore must succeed: {0}", restore.Stderr);

        (await ScalarAsync($"SELECT count(*) FROM ops.audit_log WHERE \"Action\" = '{marker}';"))
            .Should().Be(1L, "the row written before the backup has to come back");

        var tamper = async () => await ExecuteAsync(
            $"UPDATE ops.audit_log SET \"Action\" = 'tampered' WHERE \"Action\" = '{marker}';");

        (await tamper.Should().ThrowAsync<PostgresException>(
                "the append-only trigger is not a row, so a restore that returned every row without it "
                + "would look like a complete success while having dropped the control behind FR-AUD-002"))
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
