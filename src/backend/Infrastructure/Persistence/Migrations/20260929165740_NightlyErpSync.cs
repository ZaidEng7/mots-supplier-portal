// Adds the sync's memory of what the ERP turned away, and the last run's outcome on the connection.
//
// A SUPPLIER THE IMPORT ALREADY SUSPENDED IS REMEMBERED AS SUSPENDED BY IT. Before this column existed the import
// suspended a supplier the ERP had disabled or not approved and remembered nothing, and it wrote no audit row for it -
// a person's suspension, or the expiry rule's, always does. Starting such a supplier as NotDisabled would have the
// first run take it for one already out of service for another reason and only mark it, and a person who then
// reinstated it would be overruled within the hour.

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
                SET "ErpDisabledState" = 'SuspendedAsDisabled'
                WHERE s."ExternalId" IS NOT NULL
                  AND s."LifecycleState" = 'Suspended'
                  AND NOT EXISTS (
                      SELECT 1 FROM ops.audit_log a
                      WHERE a."AggregateId" = s."Id" AND a."ToState" = 'Suspended');
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
