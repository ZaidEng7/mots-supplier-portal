namespace MotsSupplierPortal.Application.Dashboards;

/// <summary>SCR-120's KPI row (SCREEN-SPECIFICATIONS.md §1): four tiles.</summary>
public sealed record SupplierKpisDto(
    int OpenInvitations,
    int DraftProposals,
    int SubmittedProposals,
    int DocumentsNeedingAttention);

/// <summary>One row of §1's "Invitations &amp; deadlines (top 5)".</summary>
public sealed record DashboardInvitationDto(
    string RfqReferenceCode,
    string TitleAr,
    string TitleEn,
    string InvitationStatus,
    DateTimeOffset? SubmissionClosesAt);

/// <summary>One row of §1's "Active proposals with state and validity countdown".</summary>
public sealed record DashboardProposalDto(
    string ProposalReferenceCode,
    string RfqReferenceCode,
    string TitleAr,
    string TitleEn,
    string State,
    DateOnly? ValidityEnd);

/// <summary>
/// §1's "Profile &amp; document health card (completeness meter, next required document)".
///
/// <para><b>§12.2 shows <c>profileCompleteness: 0.62</c> on the supplier response and no code has
/// ever produced it</b> - the field does not exist. It is computed here instead, and its definition
/// is an INVENTION: required documents supplied over required documents total. That is the one
/// completeness this codebase can already measure (DocumentCompletenessEvaluator), and §12.2 shows a
/// number without saying what it counts.</para>
/// </summary>
public sealed record ProfileHealthDto(
    double Completeness,
    int RequiredDocumentsTotal,
    int RequiredDocumentsSupplied,
    string? NextRequiredDocumentTypeCode);

/// <summary>
/// §1's action-required strip: "expiring or rejected documents, invitations closing soon,
/// clarifications answered, award offers".
///
/// <para>Each condition is reported as a COUNT rather than a boolean so the chip can say how many,
/// and so a chip cannot appear for a condition that has since resolved to zero.</para>
/// </summary>
public sealed record ActionRequiredDto(
    int ExpiringDocuments,
    int RejectedDocuments,
    int InvitationsClosingSoon,
    int ClarificationsAnswered,
    int AwardOffers);

public sealed record SupplierDashboardDto(
    string SupplierReferenceCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string OnboardingState,
    string LifecycleState,
    /// <summary>
    /// §1's "Not-yet-approved" state: "dashboard replaced by onboarding progress banner linking to
    /// SCR-100". Sent as a flag so the client renders a DIFFERENT SCREEN rather than this one with
    /// empty widgets - a supplier who is not yet eligible for any invitation must not be shown
    /// "Open invitations: 0", which reads as "nobody wants you".
    /// </summary>
    bool IsApproved,
    SupplierKpisDto Kpis,
    ActionRequiredDto ActionRequired,
    IReadOnlyList<DashboardInvitationDto> Invitations,
    IReadOnlyList<DashboardProposalDto> Proposals,
    ProfileHealthDto ProfileHealth,
    /// <summary>§1's "ERP-degraded" state. True when this supplier's own award failed to sync.</summary>
    bool ErpDegraded);

public interface ISupplierDashboardHandler
{
    Task<SupplierDashboardDto?> HandleAsync(CancellationToken ct);
}
