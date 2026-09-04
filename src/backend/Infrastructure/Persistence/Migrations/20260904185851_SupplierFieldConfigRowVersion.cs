using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierFieldConfigRowVersion : Migration
    {
        /// <summary>
        /// T-029: SupplierFieldConfig joins §8.1's concurrency contract. Intentionally EMPTY, for the
        /// same reason 20260904000609_OfferingRowVersion is - <c>xmin</c> is a PostgreSQL system
        /// column that exists from the moment the table does, so mapping it changes the EF model and
        /// not the schema. The migration exists only so the snapshot matches the context; without it
        /// every test run fails with PendingModelChangesWarning.
        ///
        /// <para>The scaffolder emitted <c>AddColumn&lt;uint&gt;(name: "xmin", type: "xid")</c>
        /// again, which would try to create a column that already exists. Same correction, second
        /// occurrence - worth noting that the scaffolder will do this every time a versioned root is
        /// added to an existing table.</para>
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
