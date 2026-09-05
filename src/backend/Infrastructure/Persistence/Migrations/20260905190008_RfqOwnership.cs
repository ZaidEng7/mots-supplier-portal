using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RfqOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedApproverUserId",
                schema: "rfq",
                table: "rfq_approval",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                schema: "rfq",
                table: "rfq",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_OrganizationId_OwnerUserId",
                schema: "rfq",
                table: "rfq",
                columns: new[] { "OrganizationId", "OwnerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rfq_OrganizationId_OwnerUserId",
                schema: "rfq",
                table: "rfq");

            migrationBuilder.DropColumn(
                name: "AssignedApproverUserId",
                schema: "rfq",
                table: "rfq_approval");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "rfq",
                table: "rfq");
        }
    }
}
