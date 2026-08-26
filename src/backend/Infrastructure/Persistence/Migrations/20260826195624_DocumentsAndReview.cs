using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentsAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_type",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiryTracked = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_type", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_message", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_review_annotation",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    FlaggedProfileFields = table.Column<string[]>(type: "text[]", nullable: false),
                    FlaggedDocumentTypeIds = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_review_annotation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_document",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsLatestVersion = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RejectReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_document", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_document_document_type_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "reference",
                        principalTable: "document_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_document_supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "supplier",
                        principalTable: "supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "reference",
                table: "document_type",
                columns: new[] { "Id", "Code", "ExpiryTracked", "IsActive", "IsRequired", "NameAr", "NameEn" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000101"), "commercial_registration", false, true, true, "السجل التجاري", "Commercial Registration" },
                    { new Guid("00000000-0000-0000-0000-000000000102"), "tax_certificate", true, true, true, "الشهادة الضريبية", "Tax Certificate" },
                    { new Guid("00000000-0000-0000-0000-000000000103"), "chamber_membership", true, true, false, "عضوية الغرفة التجارية", "Chamber of Commerce Membership" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_type_Code",
                schema: "reference",
                table: "document_type",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_message_SyncStatus",
                schema: "ops",
                table: "outbox_message",
                column: "SyncStatus");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_DocumentTypeId",
                schema: "supplier",
                table: "supplier_document",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_SupplierId_DocumentTypeId_IsLatestVersion",
                schema: "supplier",
                table: "supplier_document",
                columns: new[] { "SupplierId", "DocumentTypeId", "IsLatestVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_review_annotation_SupplierId_ResolvedAt",
                schema: "supplier",
                table: "supplier_review_annotation",
                columns: new[] { "SupplierId", "ResolvedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "supplier_document",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "supplier_review_annotation",
                schema: "supplier");

            migrationBuilder.DropTable(
                name: "document_type",
                schema: "reference");
        }
    }
}
