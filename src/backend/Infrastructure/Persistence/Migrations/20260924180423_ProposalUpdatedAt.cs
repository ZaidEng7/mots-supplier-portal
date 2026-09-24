// Gives every bid a last-modified stamp, and tells the truth about the ones that already exist.
//
// The column cannot be null, so the generator picked year 1 as its default. Left that way, every proposal
// written before today would claim it was last modified two thousand years ago - which is not merely untidy:
// the ministry's incremental feed filters on this value, and a date nothing can be earlier than is a row that
// no incremental pull will ever return again.
//
// So existing rows are backfilled to the truest thing this database knows about them: the moment their state
// last changed, or failing that the moment they were created. That is not necessarily when the bid was last
// edited - a price corrected without a state change left no trace before this column existed, and nothing can
// recover it now - but it is never later than the truth, so an incremental caller re-reads a row it need not
// rather than missing one it needs.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProposalUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "proposal",
                table: "proposal",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(
                """
                UPDATE proposal.proposal
                SET "UpdatedAt" = COALESCE("StateChangedAt", "CreatedAt")
                WHERE "UpdatedAt" = '0001-01-01 00:00:00+00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "proposal",
                table: "proposal");
        }
    }
}
