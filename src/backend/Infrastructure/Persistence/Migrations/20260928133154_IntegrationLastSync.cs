using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationLastSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                schema: "ops",
                table: "integration_connection",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastSyncSucceeded",
                schema: "ops",
                table: "integration_connection",
                type: "boolean",
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
                columns: new[] { "LastSyncAt", "LastSyncSucceeded", "LastSyncSummary" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                schema: "ops",
                table: "integration_connection");

            migrationBuilder.DropColumn(
                name: "LastSyncSucceeded",
                schema: "ops",
                table: "integration_connection");

            migrationBuilder.DropColumn(
                name: "LastSyncSummary",
                schema: "ops",
                table: "integration_connection");
        }
    }
}
