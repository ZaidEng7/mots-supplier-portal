using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// §12-A/C: adds <c>rfq.rfq.PublishedAt</c> and backfills it from the audit trail.
    ///
    /// <para><b>Why a backfill is possible at all.</b> Publishing already wrote an audit row -
    /// <c>Action = 'rfq_published'</c>, <c>FromState = 'Approved'</c>, <c>ToState = 'Published'</c>
    /// (RfqHandlers) - and <c>ops.audit_log</c> is retained indefinitely (ASM-085). So the real
    /// publication instant is recoverable exactly, rather than approximated from CreatedAt, which
    /// would have been wrong by however long an RFQ sat in Draft and InternalReview.</para>
    ///
    /// <para><b>MIN, not MAX.</b> An RFQ should have exactly one rfq_published row, but if a
    /// re-publish ever wrote a second one, the FIRST publication is the one the field means.</para>
    ///
    /// <para><b>Reversible.</b> Down drops the column. That loses the backfilled values, but they
    /// are derived rather than authored - re-running Up reconstructs them from the audit rows,
    /// which Down does not touch. No data that exists only in this column is destroyed.</para>
    /// </summary>
    public partial class Section12aPublishedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                schema: "rfq",
                table: "rfq",
                type: "timestamp with time zone",
                nullable: true);

            // Backfilled in SQL rather than in application code: this must run inside the migration
            // transaction, against every row, on any environment the migration reaches - not only
            // where someone remembers to run a one-off job.
            migrationBuilder.Sql("""
                UPDATE rfq.rfq AS r
                SET "PublishedAt" = a.first_published
                FROM (
                    SELECT "AggregateId" AS rfq_id, MIN("OccurredAt") AS first_published
                    FROM ops.audit_log
                    WHERE "AggregateType" = 'Rfq' AND "Action" = 'rfq_published'
                    GROUP BY "AggregateId"
                ) AS a
                WHERE r."Id" = a.rfq_id AND r."PublishedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "rfq",
                table: "rfq");
        }
    }
}
