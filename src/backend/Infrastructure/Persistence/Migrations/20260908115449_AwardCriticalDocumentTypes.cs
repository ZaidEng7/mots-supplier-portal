using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// D-58: the commercial register and the tax card are award-critical. Chamber membership is not.
    ///
    /// <para><b>This is what makes BRULE-023 fire for the first time.</b> The flag has existed since
    /// 20260829102041 and every seeded type has carried <c>false</c> ever since, because which
    /// documents are award-critical is a procurement-risk judgement and no default was invented -
    /// COMPLETION-INVENTORY §4.1 recorded the rule as one that "suspends nobody". The judgement is now
    /// made: both of these are what make a company legally able to hold a contract at all - an expired
    /// commercial register means the entity is no longer registered to trade, an expired tax card
    /// means it cannot lawfully be paid. Chamber membership evidences standing rather than capacity,
    /// so its expiry is a compliance flag rather than a bar.</para>
    ///
    /// <para><b>A migration rather than a click on SCR-710.</b> An administrator can set this from the
    /// reference-data screen, and doing it there would leave every existing deployment disagreeing
    /// with every new one about which documents suspend a supplier. The seeded value is the product's
    /// answer; the screen is for a buying body that needs a different one.</para>
    ///
    /// <para><b>Ordering.</b> This could not safely have shipped before batch 13: until then an
    /// approved supplier could not upload a replacement document and no reviewer could reopen them, so
    /// a supplier suspended by this rule had no route back except a database edit.</para>
    /// </summary>
    public partial class AwardCriticalDocumentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "reference",
                table: "document_type",
                keyColumn: "Id",
                keyValue: new System.Guid("00000000-0000-0000-0000-000000000101"),
                column: "IsAwardCritical",
                value: true);

            migrationBuilder.UpdateData(
                schema: "reference",
                table: "document_type",
                keyColumn: "Id",
                keyValue: new System.Guid("00000000-0000-0000-0000-000000000102"),
                column: "IsAwardCritical",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible as data, and worth saying what reversing does NOT do: a supplier suspended
            // while the flag was set stays suspended, because the suspension is a fact about their
            // documents rather than a projection of this column.
            migrationBuilder.UpdateData(
                schema: "reference",
                table: "document_type",
                keyColumn: "Id",
                keyValue: new System.Guid("00000000-0000-0000-0000-000000000101"),
                column: "IsAwardCritical",
                value: false);

            migrationBuilder.UpdateData(
                schema: "reference",
                table: "document_type",
                keyColumn: "Id",
                keyValue: new System.Guid("00000000-0000-0000-0000-000000000102"),
                column: "IsAwardCritical",
                value: false);
        }
    }
}
