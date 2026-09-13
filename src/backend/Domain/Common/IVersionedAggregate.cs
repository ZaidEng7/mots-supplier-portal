// A record that counts its own edits, so two saves cannot silently overwrite each other.
//
// Every save increments RowVersion. A caller who read version 4 and then saves against
// version 5 is refused, because somebody changed the record in between. That number
// travels to the browser inside an ETag and comes back as If-Match on the next write.
//
// Thirteen records implement this: Supplier, Rfq, Proposal, Evaluation,
// EvaluationTemplate, Award, Offering, SupplierFieldConfig, Notification,
// NotificationTemplate, SystemSetting, EmailTemplateOverride, UiStringOverride. An
// editable record that does NOT implement it is unprotected rather than exempt - nothing
// can stop two people overwriting each other on it. VersionedRootCountTests derives the
// count from the code, because this note once said nine while there were thirteen, and a
// reader auditing "the nine" would have found them consistent and never looked at the
// other four.
//
// The number is counted by the application, not by Postgres xmin. That was the original
// design and it was wrong: xmin only advances when the root row itself is written, and
// adding a child row does not write it, so a correct If-Match on a route that only
// touched children was silently skipped and two callers editing different children both
// won. xmin also cannot be assigned, so the application had no way to force it forward.
//
// The setter is private on every record. Only AppDbContext writes it, inside
// SaveChangesAsync, so no handler can invent a version.

namespace MotsSupplierPortal.Domain.Common;

public interface IVersionedAggregate
{
    uint RowVersion { get; }
}
