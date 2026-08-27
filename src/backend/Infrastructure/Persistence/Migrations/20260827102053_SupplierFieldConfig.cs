using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierFieldConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_field_config",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FieldCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_field_config", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "ops",
                table: "supplier_field_config",
                columns: new[] { "Id", "Category", "FieldCode", "IsEnabled" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000401"), "ComplianceRetrigger", "legalInfo", true },
                    { new Guid("00000000-0000-0000-0000-000000000402"), "ComplianceRetrigger", "bankAccount", true },
                    { new Guid("00000000-0000-0000-0000-000000000403"), "ComplianceRetrigger", "categoryLink", true },
                    { new Guid("00000000-0000-0000-0000-000000000411"), "LegalInfoRequired", "legalNameAr", true },
                    { new Guid("00000000-0000-0000-0000-000000000412"), "LegalInfoRequired", "legalNameEn", true },
                    { new Guid("00000000-0000-0000-0000-000000000413"), "LegalInfoRequired", "registrationNumber", false },
                    { new Guid("00000000-0000-0000-0000-000000000414"), "LegalInfoRequired", "taxId", false },
                    { new Guid("00000000-0000-0000-0000-000000000415"), "LegalInfoRequired", "supplierType", false },
                    { new Guid("00000000-0000-0000-0000-000000000416"), "LegalInfoRequired", "establishedOn", false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_field_config_Category_FieldCode",
                schema: "ops",
                table: "supplier_field_config",
                columns: new[] { "Category", "FieldCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_field_config",
                schema: "ops");
        }
    }
}
