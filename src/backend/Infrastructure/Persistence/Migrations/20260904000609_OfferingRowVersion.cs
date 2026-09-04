using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferingRowVersion : Migration
    {
        /// <summary>
        /// T-029: Offering joins §8.1's concurrency contract. Intentionally EMPTY - there is no DDL
        /// to run.
        ///
        /// <para><c>xmin</c> is a PostgreSQL system column present on every table from the moment it
        /// is created, and mapping it is a change to the EF model rather than to the schema. This
        /// migration exists only so the model snapshot matches the context; without it every test
        /// run fails with PendingModelChangesWarning.</para>
        ///
        /// <para><b>What EF generated here was wrong and was replaced.</b> The scaffolder emitted
        /// <c>AddColumn&lt;uint&gt;(name: "xmin", type: "xid")</c>, which would try to create a
        /// column that already exists. Every other versioned aggregate in this codebase acquired its
        /// xmin mapping inside a <c>CreateTable</c>, where Npgsql recognises the system column and
        /// emits nothing for it - this is the first time one has been added to a table that already
        /// existed, so it is the first time the scaffolder had to be corrected.</para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
