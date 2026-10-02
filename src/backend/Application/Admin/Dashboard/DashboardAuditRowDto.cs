// One audit row as the administrator's dashboard shows it, in the security section's sensitive changes and in the
// recent activity feed. Both are hidden from a viewer without audit.read.
//
// It is a projection of its own rather than the audit search's AuditLogEntryDto, because it answers a question that
// one does not: who did it, by name. ActorName is the person's current full name when the row names an account
// that still exists, the label the row was written with when it does not, such as an integration's key name, and
// "system" when the row carries neither. ActorKind says which of those it is, so a screen does not have to guess
// whether "system" is a person who happens to be called that.
//
// Action is the stored action, exactly as written, and the screen labels it in both languages from the catalogue in
// src/frontend/src/api/dashboardAuditActions.ts. Every action the code writes has a label there, which
// DashboardAuditActionLabelTests holds to.
//
// It carries no reason, no field-level changes and no states. A reason is free text somebody typed, and the
// changes are a record's own fields; the audit search is where those are read, by somebody looking for them, and
// not on a screen that refreshes itself every minute. That also keeps every award amount off this screen, as
// Phase 1 of the dashboard requires.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

using MotsSupplierPortal.Domain.Audit;

public sealed record DashboardAuditRowDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    AuditActorKind ActorKind,
    string ActorName,
    string AggregateType,
    string? ReferenceCode);
