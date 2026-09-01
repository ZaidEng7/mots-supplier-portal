using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewQueueAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                schema: "supplier",
                table: "supplier",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedReviewerId",
                schema: "supplier",
                table: "supplier",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "AssignedReviewerId",
                schema: "supplier",
                table: "supplier");
        }
    }
}
