// Adds what the portal needs to create an approved supplier in the ERP: the push's own state on the supplier, the
// write switch and the default group on the connection, and a guarantee that no two suppliers share an ERP identifier.
//
// THE PUSH HAS ITS OWN STATE, NOT SyncStatus. SyncStatus also holds the import's memory of a supplier leaving the
// ERP, and a push writing it would undo that memory. Every existing supplier starts as NotRequested with no attempts:
// the push acts only on a supplier approved from now on that has no ERP identifier yet.
//
// THE SWITCH STARTS OFF. Adding the columns creates nothing in the ERP; a system administrator turns the push on,
// with the group, on Connected systems.
//
// ONE SUPPLIER PER ERP IDENTIFIER. The import matches on ExternalId, and a second supplier carrying the same one would
// be updated, suspended and released together with the first. Nothing enforced it before, so the migration looks
// first and stops, naming the identifiers, rather than let the index fail with a bare constraint error.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpSupplierPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ErpPushAttempts",
                schema: "supplier",
                table: "supplier",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ErpPushLastError",
                schema: "supplier",
                table: "supplier",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErpPushNextAttemptAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErpPushRequestedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ErpPushStartedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErpPushStatus",
                schema: "supplier",
                table: "supplier",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequested");

            migrationBuilder.AddColumn<bool>(
                name: "CreateSuppliersInErp",
                schema: "ops",
                table: "integration_connection",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultSupplierGroup",
                schema: "ops",
                table: "integration_connection",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "ops",
                table: "integration_connection",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000901"),
                columns: new[] { "CreateSuppliersInErp", "DefaultSupplierGroup" },
                values: new object[] { false, null });

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE duplicates text;
                BEGIN
                    SELECT string_agg("ExternalId", ', ') INTO duplicates
                    FROM (
                        SELECT "ExternalId" FROM supplier.supplier
                        WHERE "ExternalId" IS NOT NULL
                        GROUP BY "ExternalId" HAVING count(*) > 1) d;
                    IF duplicates IS NOT NULL THEN
                        RAISE EXCEPTION 'Suppliers share an ERP identifier (%). Resolve them before this migration: the import matches on it.', duplicates;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ExternalId",
                schema: "supplier",
                table: "supplier",
                column: "ExternalId",
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_ExternalId",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushAttempts",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushLastError",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushNextAttemptAt",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushRequestedAt",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushStartedAt",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "ErpPushStatus",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "CreateSuppliersInErp",
                schema: "ops",
                table: "integration_connection");

            migrationBuilder.DropColumn(
                name: "DefaultSupplierGroup",
                schema: "ops",
                table: "integration_connection");
        }
    }
}
