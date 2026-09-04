using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalAwardOffer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AwardOfferedAt",
                schema: "proposal",
                table: "proposal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                schema: "proposal",
                table: "proposal",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeclinedAt",
                schema: "proposal",
                table: "proposal",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwardOfferedAt",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "DeclinedAt",
                schema: "proposal",
                table: "proposal");
        }
    }
}
