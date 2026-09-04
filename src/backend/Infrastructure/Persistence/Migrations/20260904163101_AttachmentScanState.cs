using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentScanState : Migration
    {
        /// <summary>
        /// D-10: every existing RFQ attachment and proposal document enters as <c>PendingScan</c>.
        ///
        /// <para>NOT Clean. The decision is explicit that rows predating the scan are not assumed
        /// clean - "we never looked" and "we looked and it was fine" are different facts, and a
        /// backfill to Clean would record the second when only the first is true. Every existing
        /// attachment is therefore unservable until something scans it, which is the fail-closed
        /// half of the decision doing its job rather than a migration defect.</para>
        ///
        /// <para>EF scaffolded <c>defaultValue: ""</c>, which is not a member of the enum and would
        /// have left every existing row in a state the application cannot read.</para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScanState",
                schema: "rfq",
                table: "rfq_attachment",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PendingScan");

            migrationBuilder.AddColumn<string>(
                name: "ScanState",
                schema: "proposal",
                table: "proposal_document",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PendingScan");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScanState",
                schema: "rfq",
                table: "rfq_attachment");

            migrationBuilder.DropColumn(
                name: "ScanState",
                schema: "proposal",
                table: "proposal_document");
        }
    }
}
