using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceCodeCounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reference_code_counter",
                schema: "supplier",
                columns: table => new
                {
                    Prefix = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reference_code_counter", x => x.Prefix);
                });

            // Seed each prefix from the HIGHEST code already issued, not from the row count.
            //
            // This is the part of the migration that matters. An empty counter would restart every
            // prefix at 1 and re-issue codes that are already in use, published in URLs and recorded
            // in the audit log - the exact defect being fixed, made worse. MAX() rather than COUNT()
            // because the two disagree wherever a supplier has been deleted, which is how this was
            // discovered: 25 rows but a highest code of SUP-2026-000026.
            //
            // Prefix and sequence are split by pattern rather than by fixed offsets so the migration
            // does not silently corrupt anything whose format differs; rows that do not match the
            // TYPE-YEAR-SEQ shape are ignored rather than parsed into a wrong number.
            migrationBuilder.Sql("""
                INSERT INTO supplier.reference_code_counter ("Prefix", "LastValue")
                SELECT substring("ReferenceCode" from '^(.*-)[0-9]+$'),
                       MAX(CAST(substring("ReferenceCode" from '([0-9]+)$') AS bigint))
                FROM supplier.supplier
                WHERE "ReferenceCode" ~ '^.*-[0-9]+$'
                GROUP BY 1
                ON CONFLICT ("Prefix") DO UPDATE
                    SET "LastValue" = GREATEST(
                        supplier.reference_code_counter."LastValue", EXCLUDED."LastValue");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reference_code_counter",
                schema: "supplier");
        }
    }
}
