using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// T3-03: unique(rfq_id, supplier_id) becomes unique among proposals that are NOT withdrawn.
    ///
    /// <para>BUSINESS-PROCESSES.md §4.1 permits a supplier who withdraws to re-enter while the
    /// window is open - <i>"Release from consideration; re-submission allowed while window open
    /// (new draft)"</i> - and the unfiltered index made that impossible at the database level. The
    /// rule being enforced was always "one LIVE proposal per supplier per RFQ"; a withdrawn proposal
    /// is a historical record, not a current bid.</para>
    ///
    /// <para><b>Down is not always applicable.</b> Reverting re-imposes uniqueness across ALL
    /// proposals, which will fail if any supplier has withdrawn and re-entered by then. That is
    /// inherent to narrowing a constraint and is stated rather than hidden: the rollback needs the
    /// duplicate withdrawn rows dealt with first, and no automatic choice about which to keep would
    /// be safe to make here.</para>
    /// </summary>
    public partial class ProposalReentryAfterWithdrawal : Migration
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
                filter: "\"State\" <> 'Withdrawn'");
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
                unique: true);
        }
    }
}
