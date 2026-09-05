using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalLapsedAndCancelled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true,
                filter: "\"State\" NOT IN ('Withdrawn', 'Lapsed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true,
                filter: "\"State\" <> 'Withdrawn'");
        }
    }
}
