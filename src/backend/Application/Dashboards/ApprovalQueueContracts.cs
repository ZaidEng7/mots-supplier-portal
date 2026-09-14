// The vocabulary for the manager's approval queues: tenders waiting to be published and awards waiting to
// be approved.
//
// Each row carries the address it opens, returned by the server rather than assembled by the client.
//
// That is a defence rather than a convenience. An earlier queue listed work the persona holding it could
// not then reach, and the cheapest protection is for the queue and the link to come from the same place, so
// a test can follow exactly what the row offers.

namespace MotsSupplierPortal.Application.Dashboards;

public sealed record ApprovalQueueItemDto(
    string RfqReferenceCode,
    string TitleAr,
    string TitleEn,
    string State,
    DateTimeOffset? WaitingSince,
    string Href);

public sealed record ApprovalQueuesDto(
    IReadOnlyList<ApprovalQueueItemDto> RfqPublishApprovals,
    IReadOnlyList<ApprovalQueueItemDto> AwardApprovals);

public interface IApprovalQueuesHandler
{
    Task<ApprovalQueuesDto?> HandleAsync(CancellationToken ct);
}
