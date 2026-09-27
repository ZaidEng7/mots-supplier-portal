using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyrianGovernorates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "reference",
                table: "region",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000205"), "DAR", true, "درعا", "Daraa" },
                    { new Guid("00000000-0000-0000-0000-000000000206"), "DEZ", true, "دير الزور", "Deir ez-Zor" },
                    { new Guid("00000000-0000-0000-0000-000000000207"), "HAS", true, "الحسكة", "Al-Hasakah" },
                    { new Guid("00000000-0000-0000-0000-000000000208"), "HMA", true, "حماة", "Hama" },
                    { new Guid("00000000-0000-0000-0000-000000000209"), "IDL", true, "إدلب", "Idlib" },
                    { new Guid("00000000-0000-0000-0000-000000000210"), "QUN", true, "القنيطرة", "Quneitra" },
                    { new Guid("00000000-0000-0000-0000-000000000211"), "RAQ", true, "الرقة", "Raqqa" },
                    { new Guid("00000000-0000-0000-0000-000000000212"), "RDM", true, "ريف دمشق", "Rif Dimashq" },
                    { new Guid("00000000-0000-0000-0000-000000000213"), "SUW", true, "السويداء", "As-Suwayda" },
                    { new Guid("00000000-0000-0000-0000-000000000214"), "TAR", true, "طرطوس", "Tartus" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000205"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000206"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000207"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000208"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000209"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000210"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000211"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000212"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000213"));

            migrationBuilder.DeleteData(
                schema: "reference",
                table: "region",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000214"));
        }
    }
}
