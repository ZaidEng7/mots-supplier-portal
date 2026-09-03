namespace MotsSupplierPortal.Application.Dashboards;

/// <summary>
/// One row of SCR-401. SCREEN-INVENTORY: "Queues: RFQ publish approvals + award approvals".
///
/// <para><c>Href</c> is the API path this row opens, returned by the server rather than assembled by
/// the client. PR #90's defect was a queue listing work its persona could not then reach, and the
/// cheapest defence is for the queue and the link to come from the same place - so a test can follow
/// exactly what the row offers.</para>
/// </summary>
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
