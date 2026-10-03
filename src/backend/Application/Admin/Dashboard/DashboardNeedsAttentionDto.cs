// The needs attention section of the administrator's dashboard: what the other five sections found that somebody
// should act on, and which checks could not run.
//
// Every item is decided on the server, by the rules in DashboardNeedsAttention, from the other sections' results,
// so the screen draws what it is given and holds no rule of its own about what is worth raising. The one figure no
// other section has, the review deadline, is counted by the section itself, as NeedsAttentionSectionHandler
// describes.
//
//
// ONE ITEM PER CHECK THAT FIRED
//
// Key names the check in lower snake case and does not change: a screen translates it, and a person can search
// for it. The keys are the constants in DashboardNeedsAttentionChecks.
//
// Count is how many things the check found, where it counts things: jobs, emails, suppliers, accounts. A check
// that is a yes or a no, such as the object store not answering, has no count, and Count is null rather than 1.
//
// Link is the root of the page where somebody would act on it. It is null when the viewer does not hold the
// permission that page needs, because a link the viewer would be refused at is worse than none, and null when no
// page shows the thing.
//
// References are the reference codes of up to five suppliers whose push to the ERP failed or stalled, each with
// a link to that supplier's review page, or null in its place for a viewer that page would refuse. Every other
// item has none, and the list is empty rather than null.
//
//
// ALL CLEAR, AND THE CHECKS THAT COULD NOT RUN
//
// ChecksNotRun names, by key, the checks whose section or ERP part failed, or whose own query failed. A failed
// section says nothing, and nothing is not the same as fine.
//
// AllClear is true only when no item fired and ChecksNotRun is empty.
//
// The specification says "all clear" appears only when every check ran, and that is read as every check THIS
// VIEWER can see. A section hidden from the viewer contributes nothing: its checks are not part of this viewer's
// dashboard, so they neither fire nor count as not run. Read the other way, a viewer without audit.read could
// never be told "all clear", because the security checks are hidden from them for good, and a phrase that can
// never appear tells nobody anything.

namespace MotsSupplierPortal.Application.Admin.Dashboard;

public sealed record DashboardNeedsAttentionDto(
    bool AllClear,
    IReadOnlyList<DashboardAttentionItemDto> Items,
    IReadOnlyList<string> ChecksNotRun);

public sealed record DashboardAttentionItemDto(
    string Key,
    int? Count,
    string? Link,
    IReadOnlyList<DashboardAttentionReferenceDto> References);

public sealed record DashboardAttentionReferenceDto(string Code, string? Link);
