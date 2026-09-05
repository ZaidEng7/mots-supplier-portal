using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppManagedRowVersion : Migration
    {
        /// <summary>
        /// T-030/D-15: the nine versioned roots move from Postgres <c>xmin</c> to an
        /// application-managed <c>RowVersion</c> column.
        ///
        /// <para><b>Hand-written, because the scaffolder emitted DDL that cannot run.</b> It produced
        /// <c>RenameColumn(name: "xmin", newName: "RowVersion")</c> for each table - but <c>xmin</c> is
        /// a PostgreSQL system column, not a real one, so there is nothing to rename and the statement
        /// would fail. It also warned about possible data loss, which is what a rename of a
        /// non-existent column looks like to the scaffolder. This is the third migration in this
        /// codebase where an xmin mapping had to be corrected by hand (see
        /// 20260904000609_OfferingRowVersion and the SupplierFieldConfig one); the pattern is that
        /// EF has no idea xmin is not ours.</para>
        ///
        /// <para><b>Every existing row starts at 1.</b> The old token was xmin, so every ETag a client
        /// is holding right now becomes invalid the moment this deploys - those callers get a 412 and
        /// re-read, which is exactly the recovery §8.1 defines for a stale precondition. There is no
        /// mapping from an xmin value to a counter, and inventing one (seeding the counter from xmin)
        /// would produce versions that look like real history and are not.</para>
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "ops",
                table: "supplier_field_config",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "supplier",
                table: "supplier",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "rfq",
                table: "rfq",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "proposal",
                table: "proposal",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "supplier",
                table: "offering",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "shared",
                table: "notification",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "evaluation",
                table: "evaluation_template",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "evaluation",
                table: "evaluation",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                schema: "award",
                table: "award",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "ops",
                table: "supplier_field_config");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "supplier",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "rfq",
                table: "rfq");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "proposal",
                table: "proposal");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "supplier",
                table: "offering");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "shared",
                table: "notification");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "evaluation",
                table: "evaluation_template");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "evaluation",
                table: "evaluation");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "award",
                table: "award");
        }
    }
}
