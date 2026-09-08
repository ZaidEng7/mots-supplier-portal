using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// D-66: the Ministry may see commercial figures. The flag D-6 seeded OFF is switched on.
    ///
    /// <para><b>The seeded default stays FALSE, deliberately.</b> A fresh database still withholds until
    /// somebody decides otherwise - that is BRULE-087's aggregate-only default and it is the right posture
    /// for a product, not a deployment. This migration is one deployment's decision, applied after the seed,
    /// and reversible by Down().</para>
    ///
    /// <para><b>What reversing does NOT do.</b> Switching the flag off stops new disclosure; it does not
    /// un-show what has been read. D-57 required written sign-off before this - a name, a date and a scope -
    /// and D-66 records that it shipped without one, at the product owner's direction, at the widest scope
    /// offered: live tenders included, per-bidder values shown.</para>
    /// </summary>
    public partial class CommercialVisibilityOn : Migration
    {
        private static readonly Guid CommercialValuesFlag = new("00000000-0000-0000-0000-000000000421");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.UpdateData(
                schema: "ops",
                table: "supplier_field_config",
                keyColumn: "Id",
                keyValue: CommercialValuesFlag,
                column: "IsEnabled",
                value: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.UpdateData(
                schema: "ops",
                table: "supplier_field_config",
                keyColumn: "Id",
                keyValue: CommercialValuesFlag,
                column: "IsEnabled",
                value: false);
    }
}
