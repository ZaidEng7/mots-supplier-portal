using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // T-003. Every existing supplier would otherwise read "last updated in the year 1", which
            // is not a null anybody can reason about - it is a date, and a client comparing it would
            // conclude the profile is ancient rather than untouched. A supplier nobody has edited was
            // last modified when it was created, so that is what the column says.
            migrationBuilder.Sql(
                """
                UPDATE supplier.supplier SET "UpdatedAt" = "CreatedAt";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "supplier",
                table: "supplier");
        }
    }
}
