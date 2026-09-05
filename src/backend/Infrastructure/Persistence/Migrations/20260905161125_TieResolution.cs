using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TieResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TieResolutionReason",
                schema: "evaluation",
                table: "consolidated_result",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TieResolvedByUserId",
                schema: "evaluation",
                table: "consolidated_result",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TieUnresolved",
                schema: "evaluation",
                table: "consolidated_result",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TieResolutionReason",
                schema: "evaluation",
                table: "consolidated_result");

            migrationBuilder.DropColumn(
                name: "TieResolvedByUserId",
                schema: "evaluation",
                table: "consolidated_result");

            migrationBuilder.DropColumn(
                name: "TieUnresolved",
                schema: "evaluation",
                table: "consolidated_result");
        }
    }
}
