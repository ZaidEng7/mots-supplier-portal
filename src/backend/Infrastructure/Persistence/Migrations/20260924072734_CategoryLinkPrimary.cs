// Adds the primary-category flag, and chooses one for every supplier that already had categories.
//
// THE BACKFILL RUNS BETWEEN THE COLUMN AND THE INDEX, and that order is the whole reason this migration is
// hand-edited. The index permits one primary row per supplier; adding it first would be satisfied by every
// supplier having none, and the update that follows would then have to be correct on the first attempt or
// fail halfway. Filling the rows first and constraining afterwards means the index is proof the backfill
// worked rather than a thing the backfill has to survive.
//
// FIRST BY CODE, ALPHABETICALLY, because there is nothing better available. A supplier with several
// categories has never been asked which one is the main one - the field did not exist - so any choice here
// is made on their behalf. Alphabetical is at least deterministic and repeatable on every environment,
// which "whichever row the database returns first" is not. Twenty-two of the thirty-eight suppliers in a
// seeded database have more than one category, so this is most of them, and they can change it on their
// profile.
//
// Suppliers with exactly one category get that one, which is not a guess at all. Suppliers with none get
// nothing, and gain a primary when they link their first.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoryLinkPrimary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrimary",
                schema: "supplier",
                table: "category_link",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE supplier.category_link AS c
                SET "IsPrimary" = true
                WHERE c."Id" = (
                    SELECT inner_link."Id"
                    FROM supplier.category_link AS inner_link
                    WHERE inner_link."SupplierId" = c."SupplierId"
                    ORDER BY inner_link."CategoryCode"
                    LIMIT 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "ix_category_link_one_primary_per_supplier",
                schema: "supplier",
                table: "category_link",
                column: "SupplierId",
                unique: true,
                filter: "\"IsPrimary\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_category_link_one_primary_per_supplier",
                schema: "supplier",
                table: "category_link");

            migrationBuilder.DropColumn(
                name: "IsPrimary",
                schema: "supplier",
                table: "category_link");
        }
    }
}
