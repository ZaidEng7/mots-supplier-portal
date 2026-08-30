using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// FR-REG-004: duplicate-prevention blocks a registration "with the same legal identifier or
    /// email already exists (case/whitespace-normalized)". Email dedupe (Identity's own unique
    /// index) existed; the legal-identifier half did not - two suppliers could register with the
    /// identical RegistrationNumber and nothing in the database or the handler stopped it.
    ///
    /// <para><b>Normalization: whitespace-trimmed, case-SENSITIVE.</b> Not the same normalization
    /// as email. BRULE-005 is explicit that identifier presence/format is "configuration, not
    /// hard-coded law" - this codebase does not know, and is not supposed to guess, what a valid
    /// Syrian commercial-registration number looks like (docs/product/ASSUMPTIONS.md ASM-020).
    /// Case-folding a value whose format is deliberately unconstrained risks merging two
    /// legitimately distinct identifiers that happen to share a case-insensitive spelling - a
    /// false-positive collision is worse here than in an email dedupe, because unlike an email
    /// address there is no user-facing convention establishing that case is insignificant.
    /// Trimming surrounding whitespace is the one normalization the requirement names explicitly
    /// ("whitespace-normalized") and carries no such risk.</para>
    ///
    /// <para><b>An expression index, not a plain column index.</b> A plain unique index on
    /// "RegistrationNumber" would let "12345" and "12345 " (trailing space) coexist, which is
    /// exactly the pair the requirement says must collide. Indexing btrim("RegistrationNumber")
    /// enforces the normalized comparison as a database constraint, not only as something the
    /// current C# handler happens to do before every insert - a direct SQL write or a future
    /// handler that forgets to trim is still caught.</para>
    ///
    /// <para>NULL is handled for free: Postgres never considers two NULLs equal in a unique index,
    /// so any number of suppliers with no RegistrationNumber at all coexist without special-casing
    /// - correct, since the field is optional at registration (Supplier.Register(string?
    /// registrationNumber)).</para>
    /// </summary>
    public partial class SupplierRegistrationNumberUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_supplier_RegistrationNumber_Normalized"
                ON supplier.supplier (btrim("RegistrationNumber"))
                WHERE "RegistrationNumber" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS supplier."IX_supplier_RegistrationNumber_Normalized";""");
        }
    }
}
