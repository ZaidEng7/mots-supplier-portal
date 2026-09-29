// Writes the audit rows the ERP import did not write for the suppliers it had already created.
//
// EVERY SUPPLIER THE IMPORT CREATED GETS THE ROW IT WOULD GET TODAY: imported from the ERP and approved without portal
// review. The import used to create suppliers silently, so the trail could not say where a supplier came from. The row
// is dated when the supplier was created, not when this runs, because that is when it happened.
//
// IT IS CREDITED TO THE RUN THAT CREATED IT: the latest run row at or before the supplier's creation, since each run
// writes its own row before its first supplier. Runs hold a lock since the sync and never overlap. The import before
// the sync had no lock, so two presses of the button could overlap and a supplier be credited to the second - which,
// as those runs named nobody, changes only whose correlation and address it carries.
//
// THE ROW SAYS ONLY WHAT THE RUN RECORDED. A person's run names that person and the hourly job names the system. The
// import before the sync recorded nobody at all, although only a person could run it; calling those rows "system"
// would credit the hourly job with suppliers it never created, permanently. So a run that recorded no actor, or no run
// at all, gives "not recorded", with the run's address as the one trace of who asked.
//
// A SUPPLIER THAT ARRIVED SUSPENDED ALSO GETS ITS SUSPENSION, under the action the sync uses for the same reason, so
// the trail says why it is out of service. It is found as an imported supplier that is suspended with no suspension
// anywhere on its trail. Every other way in writes one - a person, the expiry rule, the sync suspending a supplier
// already here, and NightlyErpSync for the ones before the sync remembered anything - so what is left arrived that way.
// Whatever the ERP has said since does not change that.
//
// THE REASON IS READ FROM WHAT IS STILL TRUE. It arrived not approved if the sync still remembers it waiting, or its
// trail shows that wait was ended by a disable. Otherwise it arrived disabled: a supplier that arrived waiting is
// released, still waiting, withdrawn or held by a person, and every one of those either leaves it recognisable or
// writes a suspension of its own. The row is dated one microsecond after the creation, so the trail reads in order.
//
// ONLY THE IMPORT SETS AN ERP IDENTIFIER when this runs, so "has an ERP identifier" means "created by the import".
//
// RUNNING IT AGAIN ADDS NOTHING: each insert skips a supplier that already has its row. It is a constant so the test
// runs this exact statement rather than a copy of it.
//
// THERE IS NO DOWN. The audit trail is append-only, enforced by a trigger since the first migration, and these rows are
// true records; rolling the schema back does not make them false.

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MotsSupplierPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpImportAuditTrail : Migration
    {
        public const string Backfill =
            """
            INSERT INTO ops.audit_log
                ("Id", "OccurredAt", "ActorUserId", "ActorKind", "ActorLabel", "AggregateType", "AggregateId",
                 "ReferenceCode", "Action", "FromState", "ToState", "Reason", "Changes", "CorrelationId", "IpAddress")
            SELECT gen_random_uuid(), s."CreatedAt", run."ActorUserId", COALESCE(run."ActorKind", 'System'),
                   CASE WHEN run."ActorUserId" IS NULL AND run."ActorLabel" IS NULL THEN 'not recorded' ELSE run."ActorLabel" END,
                   'Supplier', s."Id", s."ReferenceCode", 'supplier.imported_from_erp', NULL, 'Approved',
                   'Imported from the ERP, where it is ' || s."ExternalId"
                       || ', and approved without portal review. Recorded afterwards, when the import began to write this row.',
                   NULL, COALESCE(run."CorrelationId", gen_random_uuid()), run."IpAddress"
            FROM supplier.supplier s
            LEFT JOIN LATERAL (
                SELECT a."ActorUserId", a."ActorKind", a."ActorLabel", a."CorrelationId", a."IpAddress"
                FROM ops.audit_log a
                WHERE a."Action" = 'ErpImportRun' AND a."OccurredAt" <= s."CreatedAt"
                ORDER BY a."OccurredAt" DESC
                LIMIT 1) run ON true
            WHERE s."ExternalId" IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM ops.audit_log a
                  WHERE a."AggregateId" = s."Id" AND a."Action" = 'supplier.imported_from_erp');

            INSERT INTO ops.audit_log
                ("Id", "OccurredAt", "ActorUserId", "ActorKind", "ActorLabel", "AggregateType", "AggregateId",
                 "ReferenceCode", "Action", "FromState", "ToState", "Reason", "Changes", "CorrelationId", "IpAddress")
            SELECT gen_random_uuid(), s."CreatedAt" + interval '1 microsecond', run."ActorUserId",
                   COALESCE(run."ActorKind", 'System'),
                   CASE WHEN run."ActorUserId" IS NULL AND run."ActorLabel" IS NULL THEN 'not recorded' ELSE run."ActorLabel" END,
                   'Supplier', s."Id", s."ReferenceCode",
                   CASE WHEN arrival.waiting
                       THEN 'supplier.suspended_not_approved_in_erp'
                       ELSE 'supplier.suspended_disabled_in_erp'
                   END,
                   NULL, 'Suspended',
                   CASE WHEN arrival.waiting THEN 'Not approved in the ERP' ELSE 'Disabled in the ERP' END
                       || '; it arrived suspended. Recorded afterwards, when the import began to write this row.',
                   NULL, COALESCE(run."CorrelationId", gen_random_uuid()), run."IpAddress"
            FROM supplier.supplier s
            CROSS JOIN LATERAL (
                SELECT s."ErpDisabledState" = 'SuspendedAsPending'
                    OR EXISTS (
                        SELECT 1 FROM ops.audit_log a
                        WHERE a."AggregateId" = s."Id" AND a."Action" = 'supplier.erp_release_withdrawn') AS waiting) arrival
            LEFT JOIN LATERAL (
                SELECT a."ActorUserId", a."ActorKind", a."ActorLabel", a."CorrelationId", a."IpAddress"
                FROM ops.audit_log a
                WHERE a."Action" = 'ErpImportRun' AND a."OccurredAt" <= s."CreatedAt"
                ORDER BY a."OccurredAt" DESC
                LIMIT 1) run ON true
            WHERE s."ExternalId" IS NOT NULL
              AND s."LifecycleState" = 'Suspended'
              AND NOT EXISTS (
                  SELECT 1 FROM ops.audit_log a
                  WHERE a."AggregateId" = s."Id" AND a."ToState" = 'Suspended');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Backfill);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
