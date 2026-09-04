using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierDocumentReferenceCode : Migration
    {
        /// <summary>
        /// T-010: gives every SupplierDocument its public code, existing rows included.
        ///
        /// <para><b>The scaffolded version was replaced because it could not run.</b> EF emitted
        /// AddColumn(nullable: false, defaultValue: "") followed by a unique index - which succeeds
        /// on an empty table and fails on the second existing row, because every one of them would
        /// hold the same empty string. That is the shape of migration that passes CI on a fresh
        /// database and fails on the only database that matters.</para>
        ///
        /// <para><b>There is no window where a real document is unaddressable.</b> The column is
        /// added nullable, every row is backfilled, and only then is it made NOT NULL and unique -
        /// all inside this migration, which Postgres runs in one transaction, so no other session
        /// ever observes a committed row without a code. AddColumn takes ACCESS EXCLUSIVE on the
        /// table, so a concurrent insert cannot interleave with the backfill either.</para>
        ///
        /// <para><b>Deploy ordering is the one real constraint, and it is stated rather than
        /// hidden:</b> the previous application version does not set ReferenceCode, so if it were
        /// still serving writes after this migration commits, its inserts would fail the NOT NULL.
        /// This is a migrate-then-switch deployment, not a rolling one. Making the column nullable
        /// forever to allow overlap would trade a deploy constraint for a permanent one - a document
        /// with no code is unaddressable, which is the defect being closed.</para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReferenceCode",
                schema: "supplier",
                table: "supplier_document",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Deterministic and year-partitioned, matching the shape the generator produces for new
            // rows: DOC-<year of upload>-<six digits>. Ordered by UploadedAt then Id so the sequence
            // is stable and reproducible rather than dependent on physical row order.
            migrationBuilder.Sql("""
                UPDATE supplier.supplier_document AS d
                SET "ReferenceCode" =
                    'DOC-' || to_char(s.uploaded_year, '9999') || '-' || lpad(s.seq::text, 6, '0')
                FROM (
                    SELECT "Id",
                           date_part('year', "UploadedAt") AS uploaded_year,
                           row_number() OVER (
                               PARTITION BY date_part('year', "UploadedAt")
                               ORDER BY "UploadedAt", "Id") AS seq
                    FROM supplier.supplier_document
                ) AS s
                WHERE d."Id" = s."Id";
                """);

            // Advance the shared counter past everything just issued, per year.
            //
            // Without this the generator would hand the NEXT uploaded document a code that already
            // exists - the counter starts at zero for an unseen prefix, and the unique index would
            // then reject a legitimate upload. This is the same failure MSP-81 fixed for suppliers
            // by replacing COUNT(*) with a counter; reusing that counter means inheriting the
            // obligation to seed it.
            migrationBuilder.Sql("""
                INSERT INTO supplier.reference_code_counter ("Prefix", "LastValue")
                SELECT 'DOC-' || to_char(date_part('year', "UploadedAt"), '9999') || '-', COUNT(*)
                FROM supplier.supplier_document
                GROUP BY date_part('year', "UploadedAt")
                ON CONFLICT ("Prefix")
                DO UPDATE SET "LastValue" =
                    GREATEST(reference_code_counter."LastValue", EXCLUDED."LastValue");
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceCode",
                schema: "supplier",
                table: "supplier_document",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_document_ReferenceCode",
                schema: "supplier",
                table: "supplier_document",
                column: "ReferenceCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_document_ReferenceCode",
                schema: "supplier",
                table: "supplier_document");

            migrationBuilder.DropColumn(
                name: "ReferenceCode",
                schema: "supplier",
                table: "supplier_document");
        }
    }
}
