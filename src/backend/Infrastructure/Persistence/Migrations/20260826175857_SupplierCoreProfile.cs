using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierCoreProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TaxId",
                schema: "supplier",
                table: "supplier",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                schema: "supplier",
                table: "supplier",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "supplier",
                table: "supplier",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "supplier",
                table: "supplier",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "supplier",
                table: "supplier",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.AlterColumn<string>(
                name: "TaxId",
                schema: "supplier",
                table: "supplier",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
