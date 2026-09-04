using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalDocumentEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-corrected: EF scaffolds defaultValue: "" for a string-converted enum, which is
            // not a member of ProposalDocumentEnvelope and would make every existing row unreadable.
            // Commercial is the deliberate default for existing rows - a file uploaded before
            // envelopes existed was never declared technical by anyone, and D-7's whole point is
            // that an undeclared file is treated as pricing.
            migrationBuilder.AddColumn<string>(
                name: "Envelope",
                schema: "proposal",
                table: "proposal_document",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Commercial");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Envelope",
                schema: "proposal",
                table: "proposal_document");
        }
    }
}
