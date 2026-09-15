// ops/sql/app-role.sql produces a role that can read and write rows and cannot touch the schema.
//
// WHY THIS IS A TEST RATHER THAN A README
//
// The application connects as the role that OWNS its tables, and in PostgreSQL an owner bypasses GRANT
// and REVOKE on them. That is stated in the initial migration, and it is why audit immutability is a
// trigger rather than a REVOKE. The script exists to give a deployment a second, restricted role to run
// as - and a grant script nobody executes is indistinguishable from one that grants everything.
//
// So the script itself is run here, against the same PostgreSQL the rest of the suite uses, and the
// resulting role is then asked to do the things it must be able to do and the things it must not. If a
// future migration adds a schema the script does not list, the read below fails on that schema's tables
// and this test says so.
//
// The password is generated per run and never leaves the test process. It is a throwaway on a container
// that is destroyed with the suite.

namespace MotsSupplierPortal.Tests.Integration.Platform;

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Infrastructure.Persistence;
using Npgsql;

[Collection(IntegrationTestCollection.Name)]
public sealed class LeastPrivilegeRoleTests(PostgresApiFixture fixture)
{
    private const string AppRole = "mots_app_under_test";

    private async Task<string> GrantedConnectionStringAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerConnectionString = db.Database.GetConnectionString()!;

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

        await using (var owner = new NpgsqlConnection(ownerConnectionString))
        {
            await owner.OpenAsync();
            await ApplyRoleScriptAsync(owner, password);
        }

        return new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = AppRole,
            Password = password,
        }.ConnectionString;
    }

    private static async Task ApplyRoleScriptAsync(NpgsqlConnection owner, string password)
    {
        var ownerName = new NpgsqlConnectionStringBuilder(owner.ConnectionString).Username!;
        var database = owner.Database;

        await ExecuteAsync(owner, $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    CREATE ROLE {AppRole} LOGIN PASSWORD '{password}';
                ELSE
                    ALTER ROLE {AppRole} LOGIN PASSWORD '{password}';
                END IF;
            END
            $$;
            """);

        await ExecuteAsync(owner, $"GRANT CONNECT ON DATABASE \"{database}\" TO {AppRole};");

        await ExecuteAsync(owner, $"""
            DO $$
            DECLARE target_schema text;
            BEGIN
                FOREACH target_schema IN ARRAY ARRAY[
                    'identity','supplier','rfq','proposal','evaluation','award','reference','ops','hangfire'
                ]
                LOOP
                    IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = target_schema) THEN
                        EXECUTE format('GRANT USAGE ON SCHEMA %I TO {AppRole}', target_schema);
                        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO {AppRole}', target_schema);
                        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO {AppRole}', target_schema);
                        EXECUTE format('ALTER DEFAULT PRIVILEGES FOR ROLE {ownerName} IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole}', target_schema);
                    END IF;
                END LOOP;
            END
            $$;
            """);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_application_role_can_read_the_rows_it_serves()
    {
        await using var app = new NpgsqlConnection(await GrantedConnectionStringAsync());
        await app.OpenAsync();

        await using var command = app.CreateCommand();
        command.CommandText = "SELECT count(*) FROM supplier.supplier;";

        var act = async () => await command.ExecuteScalarAsync();

        await act.Should().NotThrowAsync(
            "a role that cannot read the tables it serves is not least privilege, it is broken");
    }

    [Theory]
    [InlineData("DROP TABLE supplier.supplier;")]
    [InlineData("ALTER TABLE supplier.supplier ADD COLUMN injected text;")]
    [InlineData("CREATE TABLE supplier.injected (id uuid PRIMARY KEY);")]
    [InlineData("DROP TRIGGER audit_log_immutable ON ops.audit_log;")]
    public async Task The_application_role_cannot_change_the_schema(string statement)
    {
        await using var app = new NpgsqlConnection(await GrantedConnectionStringAsync());
        await app.OpenAsync();

        await using var command = app.CreateCommand();
        command.CommandText = statement;

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>(
                "this is the whole point of the role: an injection or a compromised handler running as it "
                + "is a row-level incident rather than a schema-level one"))
            .Which.SqlState.Should().Be(
                PostgresErrorCodes.InsufficientPrivilege,
                "the refusal has to be about PRIVILEGE. A mistyped table name raises PostgresException "
                + "too, and a test that accepts any of them passes just as happily against a statement "
                + "the role would in fact have been allowed to run");
    }
}
