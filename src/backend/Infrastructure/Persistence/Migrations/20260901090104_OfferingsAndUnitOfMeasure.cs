using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferingsAndUnitOfMeasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offering",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PriceAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offering", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "unit_of_measure",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_unit_of_measure", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "unit_of_measure",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000501"), "night", true, "ليلة", "Night" },
                    { new Guid("00000000-0000-0000-0000-000000000502"), "person", true, "شخص", "Person" },
                    { new Guid("00000000-0000-0000-0000-000000000503"), "trip", true, "رحلة", "Trip" },
                    { new Guid("00000000-0000-0000-0000-000000000504"), "hour", true, "ساعة", "Hour" },
                    { new Guid("00000000-0000-0000-0000-000000000505"), "day", true, "يوم", "Day" },
                    { new Guid("00000000-0000-0000-0000-000000000506"), "unit", true, "وحدة", "Unit" },
                    { new Guid("00000000-0000-0000-0000-000000000507"), "event", true, "فعالية", "Event" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_offering_SupplierId",
                schema: "supplier",
                table: "offering",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_unit_of_measure_Code",
                schema: "reference",
                table: "unit_of_measure",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offering",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "unit_of_measure",
                schema: "reference");
        }
    }
}
