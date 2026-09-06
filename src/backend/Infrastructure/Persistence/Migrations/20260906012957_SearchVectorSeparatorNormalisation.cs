using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchVectorSeparatorNormalisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "supplier",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                oldStored: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "rfq",
                table: "rfq",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                oldStored: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "offering",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''), '[^[:alnum:]]+', ' ', 'g'))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''))",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "supplier",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"DisplayNameAr\",'') || ' ' || coalesce(\"DisplayNameEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                oldStored: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "rfq",
                table: "rfq",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"TitleAr\",'') || ' ' || coalesce(\"TitleEn\",'') || ' ' || coalesce(\"ReferenceCode\",''), '[^[:alnum:]]+', ' ', 'g'))",
                oldStored: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                schema: "supplier",
                table: "offering",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('simple', coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "to_tsvector('simple', regexp_replace(coalesce(\"NameAr\",'') || ' ' || coalesce(\"NameEn\",'') || ' ' || coalesce(\"Description\",''), '[^[:alnum:]]+', ' ', 'g'))",
                oldStored: true);
        }
    }
}
