using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Epic11Evaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluation",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consolidated_result",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicallyQualified = table.Column<bool>(type: "boolean", nullable: false),
                    TechnicalWeightedScore = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    FinancialWeightedScore = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    WeightedTotal = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consolidated_result", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consolidated_result_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_assignment",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecusedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecusalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_assignment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_assignment_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_criterion_snapshot",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    ScoringType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_criterion_snapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluation_criterion_snapshot_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluator_score",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    CommentAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CommentEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluator_score", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evaluator_score_evaluation_EvaluationId",
                        column: x => x.EvaluationId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_consolidated_result_EvaluationId_ProposalId",
                schema: "evaluation",
                table: "consolidated_result",
                columns: new[] { "EvaluationId", "ProposalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_RfqId",
                schema: "evaluation",
                table: "evaluation",
                column: "RfqId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_assignment_EvaluationId_EvaluatorUserId",
                schema: "evaluation",
                table: "evaluation_assignment",
                columns: new[] { "EvaluationId", "EvaluatorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_criterion_snapshot_EvaluationId",
                schema: "evaluation",
                table: "evaluation_criterion_snapshot",
                column: "EvaluationId");

            migrationBuilder.CreateIndex(
                name: "IX_evaluator_score_EvaluationId_EvaluatorUserId",
                schema: "evaluation",
                table: "evaluator_score",
                columns: new[] { "EvaluationId", "EvaluatorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_evaluator_score_EvaluationId_EvaluatorUserId_ProposalId_Cri~",
                schema: "evaluation",
                table: "evaluator_score",
                columns: new[] { "EvaluationId", "EvaluatorUserId", "ProposalId", "CriterionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consolidated_result",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluation_assignment",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluation_criterion_snapshot",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluator_score",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "evaluation",
                schema: "evaluation");
        }
    }
}
