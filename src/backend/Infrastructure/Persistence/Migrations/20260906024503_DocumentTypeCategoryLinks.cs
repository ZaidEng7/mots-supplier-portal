using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTypeCategoryLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_type_category",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_type_category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_type_category_document_type_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalSchema: "reference",
                        principalTable: "document_type",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_type_category_DocumentTypeId_CategoryCode",
                schema: "reference",
                table: "document_type_category",
                columns: new[] { "DocumentTypeId", "CategoryCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_type_category",
                schema: "reference");
        }
    }
}
