using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EvaluationTemplatesAndRfqAuthoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "evaluation");

            migrationBuilder.EnsureSchema(
                name: "rfq");

            migrationBuilder.CreateTable(
                name: "evaluation_template",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsReferenced = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evaluation_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rfq",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionOpensAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SubmissionClosesAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClarificationDeadlineAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluationTargetDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EvaluationTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvaluationTemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    EvaluationTemplateSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "criterion",
                schema: "evaluation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EvaluationTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Threshold = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    ScoringType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GuidanceAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GuidanceEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_criterion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_criterion_evaluation_template_EvaluationTemplateId",
                        column: x => x.EvaluationTemplateId,
                        principalSchema: "evaluation",
                        principalTable: "evaluation_template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirement",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TextEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_requirement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_requirement_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_approval",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepNo = table.Column<int>(type: "integer", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_approval", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_approval_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_attachment",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_attachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_attachment_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rfq_item",
                schema: "rfq",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SpecificationAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SpecificationEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsUnitPrice = table.Column<bool>(type: "boolean", nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rfq_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rfq_item_rfq_RfqId",
                        column: x => x.RfqId,
                        principalSchema: "rfq",
                        principalTable: "rfq",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_criterion_EvaluationTemplateId",
                schema: "evaluation",
                table: "criterion",
                column: "EvaluationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_evaluation_template_FamilyId_Version",
                schema: "evaluation",
                table: "evaluation_template",
                columns: new[] { "FamilyId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_requirement_RfqId",
                schema: "rfq",
                table: "requirement",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_OrganizationId_State",
                schema: "rfq",
                table: "rfq",
                columns: new[] { "OrganizationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_rfq_ReferenceCode",
                schema: "rfq",
                table: "rfq",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_State",
                schema: "rfq",
                table: "rfq",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_approval_RfqId_StepNo",
                schema: "rfq",
                table: "rfq_approval",
                columns: new[] { "RfqId", "StepNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_attachment_RfqId",
                schema: "rfq",
                table: "rfq_attachment",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_item_RfqId_LineNo",
                schema: "rfq",
                table: "rfq_item",
                columns: new[] { "RfqId", "LineNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "criterion",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "requirement",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "rfq_approval",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "rfq_attachment",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "rfq_item",
                schema: "rfq");

            migrationBuilder.DropTable(
                name: "evaluation_template",
                schema: "evaluation");

            migrationBuilder.DropTable(
                name: "rfq",
                schema: "rfq");
        }
    }
}
