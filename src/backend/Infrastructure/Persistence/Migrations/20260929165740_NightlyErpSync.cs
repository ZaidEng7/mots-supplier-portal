// Adds the sync's memory of what the ERP turned away, and the last run's outcome on the connection.
//
// A SUPPLIER THE IMPORT ALREADY SUSPENDED IS REMEMBERED AS SUSPENDED BY IT. Before this column existed the import
// suspended a supplier the ERP had disabled or not approved and remembered nothing, and it wrote no audit row for it -
// a person's suspension, or the expiry rule's, always does. So a suspended supplier whose latest lifecycle row is not a
// suspension - none at all, or a reinstatement since followed by the import's silent one - was suspended by the import. Starting such a supplier as NotDisabled would have the
// first run take it for one already out of service for another reason and only mark it, and a person who then
// reinstated it would be overruled within the hour.
//
// AND THE SUSPENSION IS WRITTEN TO THE AUDIT TRAIL, which the import never did. Without a row, the latest suspension
// the trail shows could be an older one by the expiry rule - which a later document approval would then lift, as if
// the renewal had been the reason the supplier was out.
//
// IT IS RECORDED AS WAITING FOR APPROVAL, the one memory the ERP's approval may lift. The import could not tell a
// disable from a pending approval afterwards, but the first run can: a supplier the ERP still disables is moved to
// SuspendedAsDisabled then, and one it has approved comes into service. The only case this gets wrong is a supplier
// disabled when it was imported and both re-enabled and approved before the first run - which the one supplier this
// applies to today, still pending in the ERP, is not.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NightlyErpSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErpDisabledState",
                schema: "supplier",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotDisabled");

            migrationBuilder.Sql(
                """
                UPDATE supplier.supplier s
                SET "ErpDisabledState" = 'SuspendedAsPending'
                WHERE s."ExternalId" IS NOT NULL
                  AND s."LifecycleState" = 'Suspended'
                  AND COALESCE((
                      SELECT a."ToState" FROM ops.audit_log a
                      WHERE a."AggregateId" = s."Id" AND a."ToState" IN ('Active', 'Suspended', 'Deactivated')
                      ORDER BY a."OccurredAt" DESC
                      LIMIT 1), 'Active') <> 'Suspended';

                INSERT INTO ops.audit_log
                    ("Id", "OccurredAt", "ActorUserId", "ActorKind", "ActorLabel", "AggregateType", "AggregateId",
                     "ReferenceCode", "Action", "FromState", "ToState", "Reason", "Changes", "CorrelationId", "IpAddress")
                SELECT gen_random_uuid(), now(), NULL, 'System', 'system', 'Supplier', s."Id",
                       s."ReferenceCode", 'supplier.suspended_not_approved_in_erp', NULL, 'Suspended',
                       'Suspended by the ERP import while the ERP had not approved or had disabled it; recorded when the sync began to remember it.',
                       NULL, gen_random_uuid(), NULL
                FROM supplier.supplier s
                WHERE s."ErpDisabledState" = 'SuspendedAsPending';
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                schema: "ops",
                table: "integration_connection",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncOutcome",
                schema: "ops",
                table: "integration_connection",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncSummary",
                schema: "ops",
                table: "integration_connection",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "ops",
                table: "integration_connection",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000901"),
                columns: new[] { "LastSyncAt", "LastSyncOutcome", "LastSyncSummary" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpDisabledState",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                schema: "ops",
                table: "integration_connection");

            migrationBuilder.DropColumn(
                name: "LastSyncOutcome",
                schema: "ops",
                table: "integration_connection");

            migrationBuilder.DropColumn(
                name: "LastSyncSummary",
                schema: "ops",
                table: "integration_connection");
        }
    }
}
