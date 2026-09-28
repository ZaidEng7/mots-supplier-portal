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
            migrationBuilder.AddColumn<bool>(
                name: "DisabledInErp",
                schema: "supplier",
                table: "supplier",
                type: "boolean",
                nullable: false,
                defaultValue: false);

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
                name: "DisabledInErp",
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
