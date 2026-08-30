using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// FR-AUD-002 / NFR-CMP-002: "no user, including admin, can edit or delete" AuditLog entries.
    /// Before this, that was true only by convention - AuditLogger never calls Update/Remove, but
    /// nothing stopped a bug, a future migration, or an engineer who does not know the convention
    /// from issuing a raw UPDATE or DELETE against the table.
    ///
    /// <para><b>Why a trigger and not a REVOKE.</b> The app connects as the `postgres` role
    /// (docker-compose.yml), which is also the role that ran every prior migration and therefore
    /// OWNS every table in this schema, `ops.audit_log` included. In Postgres, a table's owner
    /// always bypasses GRANT/REVOKE-based privilege checks on that table - `REVOKE UPDATE, DELETE`
    /// from an owning role is a documented no-op, not a weaker control. It would read as a fix in
    /// the migration diff and enforce nothing against the connection this application actually
    /// uses. A `BEFORE UPDATE OR DELETE` trigger is not a privilege check; it runs for every caller
    /// regardless of ownership, including a superuser, which is the only thing that can actually
    /// stop this connection specifically.</para>
    ///
    /// <para>INSERT is untouched - the trigger fires only on UPDATE and DELETE, so every existing
    /// audit-writing path keeps working unmodified.</para>
    /// </summary>
    public partial class AuditLogImmutabilityTrigger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ops.prevent_audit_log_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'ops.audit_log is append-only (FR-AUD-002/NFR-CMP-002): % on row % is not permitted',
                        TG_OP, OLD."Id"
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER audit_log_immutable
                BEFORE UPDATE OR DELETE ON ops.audit_log
                FOR EACH ROW
                EXECUTE FUNCTION ops.prevent_audit_log_mutation();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_immutable ON ops.audit_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS ops.prevent_audit_log_mutation();");
        }
    }
}
