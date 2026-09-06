using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FullTextSearchVectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "supplier",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "rfq",
                table: "rfq",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                stored: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "offering",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_SearchVector",
                schema: "supplier",
                table: "supplier",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_rfq_SearchVector",
                schema: "rfq",
                table: "rfq",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_offering_SearchVector",
                schema: "supplier",
                table: "offering",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_SearchVector",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropIndex(
                name: "IX_rfq_SearchVector",
                schema: "rfq",
                table: "rfq");

            migrationBuilder.DropIndex(
                name: "IX_offering_SearchVector",
                schema: "supplier",
                table: "offering");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                schema: "rfq",
                table: "rfq");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                schema: "supplier",
                table: "offering");
        }
    }
}
