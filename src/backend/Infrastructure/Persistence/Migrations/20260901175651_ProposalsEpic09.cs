using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalsEpic09 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "proposal");

            migrationBuilder.CreateTable(
                name: "proposal",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    PaymentTerms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IncotermCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DeliveryTermsAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeliveryTermsEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Warranty = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValidityStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidityEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    NarrativeAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NarrativeEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WithdrawReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "proposal_document",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_document_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "proposal_item",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    LeadTimeDays = table.Column<int>(type: "integer", nullable: true),
                    NotesAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NotesEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_item_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement_answer",
                schema: "proposal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequirementId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AnswerEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement_answer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_answer_proposal_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "proposal",
                        principalTable: "proposal",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_ReferenceCode",
                schema: "proposal",
                table: "proposal",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_State",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_RfqId_SupplierId",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposal_SupplierId_State",
                schema: "proposal",
                table: "proposal",
                columns: new[] { "SupplierId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_document_ProposalId",
                schema: "proposal",
                table: "proposal_document",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_item_ProposalId_RfqItemId",
                schema: "proposal",
                table: "proposal_item",
                columns: new[] { "ProposalId", "RfqItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_answer_ProposalId_RequirementId",
                schema: "proposal",
                table: "requirement_answer",
                columns: new[] { "ProposalId", "RequirementId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proposal_document",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "proposal_item",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "requirement_answer",
                schema: "proposal");

            migrationBuilder.DropTable(
                name: "proposal",
                schema: "proposal");
        }
    }
}
