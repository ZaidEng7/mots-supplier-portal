using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentExpiryReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_expiry_reminder",
                schema: "supplier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    ThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    WasSent = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_expiry_reminder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_expiry_reminder_supplier_document_SupplierDocument~",
                        column: x => x.SupplierDocumentId,
                        principalSchema: "supplier",
                        principalTable: "supplier_document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_expiry_reminder_SupplierDocumentId_DocumentVersion~",
                schema: "supplier",
                table: "document_expiry_reminder",
                columns: new[] { "SupplierDocumentId", "DocumentVersion", "ThresholdDays" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_expiry_reminder",
                schema: "supplier");
        }
    }
}
