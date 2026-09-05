using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeadlineChangeReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubmissionDeadlineChangeReason",
                schema: "rfq",
                table: "rfq",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmissionDeadlineChangedAt",
                schema: "rfq",
                table: "rfq",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubmissionDeadlineChangeReason",
                schema: "rfq",
                table: "rfq");

            migrationBuilder.DropColumn(
                name: "SubmissionDeadlineChangedAt",
                schema: "rfq",
                table: "rfq");
        }
    }
}
