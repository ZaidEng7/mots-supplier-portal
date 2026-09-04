using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CriterionRequiresJustification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresJustification",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresJustification",
                schema: "evaluation",
                table: "criterion",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresJustification",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot");

            migrationBuilder.DropColumn(
                name: "RequiresJustification",
                schema: "evaluation",
                table: "criterion");
        }
    }
}
