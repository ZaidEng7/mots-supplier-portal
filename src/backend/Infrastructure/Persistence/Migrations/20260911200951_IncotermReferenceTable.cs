using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncotermReferenceTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incoterm",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incoterm", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "incoterm",
                columns: new[] { "Id", "Code", "IsActive", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000601"), "EXW", true, "تسليم المصنع", "Ex Works" },
                    { new Guid("00000000-0000-0000-0000-000000000602"), "FCA", true, "تسليم الناقل", "Free Carrier" },
                    { new Guid("00000000-0000-0000-0000-000000000603"), "CPT", true, "النقل مدفوع حتى", "Carriage Paid To" },
                    { new Guid("00000000-0000-0000-0000-000000000604"), "CIP", true, "النقل والتأمين مدفوعان حتى", "Carriage and Insurance Paid To" },
                    { new Guid("00000000-0000-0000-0000-000000000605"), "DAP", true, "التسليم في المكان", "Delivered at Place" },
                    { new Guid("00000000-0000-0000-0000-000000000606"), "DPU", true, "التسليم في المكان بعد التفريغ", "Delivered at Place Unloaded" },
                    { new Guid("00000000-0000-0000-0000-000000000607"), "DDP", true, "التسليم خالص الرسوم", "Delivered Duty Paid" },
                    { new Guid("00000000-0000-0000-0000-000000000608"), "FAS", true, "التسليم بجانب السفينة", "Free Alongside Ship" },
                    { new Guid("00000000-0000-0000-0000-000000000609"), "FOB", true, "التسليم على ظهر السفينة", "Free on Board" },
                    { new Guid("00000000-0000-0000-0000-00000000060a"), "CFR", true, "التكلفة وأجرة الشحن", "Cost and Freight" },
                    { new Guid("00000000-0000-0000-0000-00000000060b"), "CIF", true, "التكلفة والتأمين وأجرة الشحن", "Cost, Insurance and Freight" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_incoterm_Code",
                schema: "reference",
                table: "incoterm",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incoterm",
                schema: "reference");
        }
    }
}
