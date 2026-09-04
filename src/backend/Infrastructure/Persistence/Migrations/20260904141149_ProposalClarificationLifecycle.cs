using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalClarificationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClarificationReason",
                schema: "proposal",
                table: "proposal",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClarificationRequestedAt",
                schema: "proposal",
                table: "proposal",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                schema: "proposal",
                table: "proposal",
                type: "integer",
                nullable: false,
                // 1, not 0. §4.1 numbers a revision n+1 from the original submission, so every row
                // that already exists IS revision 1 - a zero would say those proposals had never
                // been submitted, which is the opposite of what the column means.
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClarificationReason",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "ClarificationRequestedAt",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                schema: "proposal",
                table: "proposal");
        }
    }
}
