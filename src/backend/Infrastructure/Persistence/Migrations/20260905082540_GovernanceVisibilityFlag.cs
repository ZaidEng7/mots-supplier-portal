using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GovernanceVisibilityFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "ops",
                table: "supplier_field_config",
                columns: new[] { "Id", "Category", "FieldCode", "IsEnabled" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000421"), "GovernanceVisibility", "commercialValues", false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "ops",
                table: "supplier_field_config",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000421"));
        }
    }
}
