using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Epic14Award : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "award");

            migrationBuilder.CreateTable(
                name: "award",
                schema: "award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    WinningProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    JustificationAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    JustificationEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecommendedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecommendationRevision = table.Column<int>(type: "integer", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ComparisonSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    ErpSyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExternalPurchaseOrderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErpSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErpRetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_award", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval",
                schema: "award",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNo = table.Column<int>(type: "integer", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approval_award_AwardId",
                        column: x => x.AwardId,
                        principalSchema: "award",
                        principalTable: "award",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approval_AwardId_StepNo",
                schema: "award",
                table: "approval",
                columns: new[] { "AwardId", "StepNo" });

            migrationBuilder.CreateIndex(
                name: "IX_award_RfqId",
                schema: "award",
                table: "award",
                column: "RfqId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval",
                schema: "award");

            migrationBuilder.DropTable(
                name: "award",
                schema: "award");
        }
    }
}
