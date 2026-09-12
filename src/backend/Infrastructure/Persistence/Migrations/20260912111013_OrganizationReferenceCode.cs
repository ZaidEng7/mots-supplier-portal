using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationReferenceCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenceCode",
                schema: "organization",
                table: "organization",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // T-055. Existing buying bodies get codes BEFORE the unique index exists, because the
            // column's default is the empty string and a second organization would collide with the
            // first the moment the index is created.
            //
            // Ordered by CreatedAt so the numbering follows the order the bodies were registered in,
            // which is the only ordering that will not look arbitrary to somebody reading the list.
            migrationBuilder.Sql(
                """
                UPDATE organization.organization AS o
                SET "ReferenceCode" = 'ORG-' || to_char(o."CreatedAt", 'YYYY') || '-' || lpad(numbered.seq::text, 6, '0')
                FROM (
                    SELECT "Id", row_number() OVER (PARTITION BY to_char("CreatedAt", 'YYYY') ORDER BY "CreatedAt", "Id") AS seq
                    FROM organization.organization
                ) AS numbered
                WHERE o."Id" = numbered."Id";
                """);

            // And the shared counter is advanced past what the backfill consumed, per year. Without
            // this the next organization created would be handed ORG-2026-000001 again and fail the
            // unique index - the allocator reads this table and nothing else.
            migrationBuilder.Sql(
                """
                INSERT INTO supplier.reference_code_counter ("Prefix", "LastValue")
                SELECT 'ORG-' || to_char("CreatedAt", 'YYYY') || '-', count(*)
                FROM organization.organization
                GROUP BY to_char("CreatedAt", 'YYYY')
                ON CONFLICT ("Prefix") DO UPDATE
                SET "LastValue" = GREATEST(supplier.reference_code_counter."LastValue", EXCLUDED."LastValue");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_organization_ReferenceCode",
                schema: "organization",
                table: "organization",
                column: "ReferenceCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organization_ReferenceCode",
                schema: "organization",
                table: "organization");

            migrationBuilder.DropColumn(
                name: "ReferenceCode",
                schema: "organization",
                table: "organization");
        }
    }
}
