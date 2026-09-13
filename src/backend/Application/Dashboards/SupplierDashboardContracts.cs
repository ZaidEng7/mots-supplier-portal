// The vocabulary for the supplier's own dashboard: the figures across the top, their invitations and
// deadlines, their active bids, and the health of their profile.
//
//
// THE COMPLETENESS METER IS AN INVENTION
//
// The written contract shows a completeness figure on the supplier response and no code had ever produced
// it. The field did not exist.
//
// It is computed here, and the definition is ours: required documents supplied over required documents
// total. That is the one completeness this codebase could already measure, and the contract shows a number
// without saying what it counts.
//
//
// THE NEXT REQUIRED DOCUMENT CARRIES ITS NAME
//
// In both languages, because the code was reaching the screen. Found by reading the supplier dashboard as a
// supplier: the caption said the next required document was a database value.
//
// That is an internal code shown to a member of the public, on the one line of that panel telling them what
// to do next, which makes it the least useful place for it.

namespace MotsSupplierPortal.Application.Dashboards;

public sealed record SupplierKpisDto(
    int OpenInvitations,
    int DraftProposals,
    int SubmittedProposals,
    int DocumentsNeedingAttention);

public sealed record DashboardInvitationDto(
    string RfqReferenceCode,
    string TitleAr,
    string TitleEn,
    string InvitationStatus,
    DateTimeOffset? SubmissionClosesAt);

public sealed record DashboardProposalDto(
    string ProposalReferenceCode,
    string RfqReferenceCode,
    string TitleAr,
    string TitleEn,
    string State,
    DateOnly? ValidityEnd);

public sealed record ProfileHealthDto(
    double Completeness,
    int RequiredDocumentsTotal,
    int RequiredDocumentsSupplied,
    string? NextRequiredDocumentTypeCode,
    string? NextRequiredDocumentNameAr,
    string? NextRequiredDocumentNameEn);

public sealed record ActionRequiredDto(
    int ExpiringDocuments,
    int RejectedDocuments,
    int InvitationsClosingSoon,
    int ClarificationsAnswered,
    int AwardOffers);

public sealed record DashboardAwardDto(
    string RfqReferenceCode,
    string RfqTitleAr,
    string RfqTitleEn,
    string ProposalCode,
    string Outcome,
    DateTimeOffset? DecidedAt,
    decimal? Value,
    string? CurrencyCode);

public sealed record SupplierDashboardDto(
    string SupplierReferenceCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string OnboardingState,
    string LifecycleState,
    bool IsApproved,
    SupplierKpisDto Kpis,
    ActionRequiredDto ActionRequired,
    IReadOnlyList<DashboardInvitationDto> Invitations,
    IReadOnlyList<DashboardProposalDto> Proposals,
    ProfileHealthDto ProfileHealth,
    bool ErpDegraded,
    IReadOnlyList<DashboardAwardDto> Awards);

public interface ISupplierDashboardHandler
{
    Task<SupplierDashboardDto?> HandleAsync(CancellationToken ct);
}
