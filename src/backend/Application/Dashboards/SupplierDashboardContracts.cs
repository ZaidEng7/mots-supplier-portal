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
    string? NextRequiredDocumentTypeCode,
    /// <param name="NextRequiredDocumentNameAr">
    /// The document's own name, in both languages, because the CODE was reaching the screen.
    ///
    /// <para>Found by reading the supplier dashboard as the supplier: the caption said "Next required
    /// document: commercial_registration". That is a database value shown to a member of the public, and it is
    /// the one line on that panel telling them what to do next - so it was also the least useful place for it.
    /// The name is added rather than the SPA mapping the code, because the mapping lives in the reference
    /// table and a second copy in the frontend would drift the first time a name is corrected on SCR-710.</para>
    /// </param>
    string? NextRequiredDocumentNameAr,
    string? NextRequiredDocumentNameEn);

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

/// <summary>
/// T-039/FEAT-16.3: one award outcome on the supplier's own dashboard.
///
/// <para><b>What was missing.</b> The dashboard counted award OFFERS and listed live proposals, and
/// its proposal list excludes <c>NotSelected</c> - so a supplier who lost saw their bid disappear
/// from the dashboard with no outcome anywhere on it. The outcome existed on the proposals screen and
/// on the bid itself; the screen a supplier opens first said nothing.</para>
///
/// <para><b>Losing is an outcome.</b> This list carries every resolved bid, won or not: FEAT-16.3's
/// acceptance is "award outcomes shown", and a widget that showed only wins would be a scoreboard
/// rather than a record. A supplier deciding whether to bid again needs both.</para>
///
/// <para><b>The value is the supplier's own bid</b>, taken from their own proposal, so no two-envelope
/// question arises - it is the number they typed. Null when the bid was never priced.</para>
/// </summary>
public sealed record DashboardAwardDto(
    string RfqReferenceCode,
    string RfqTitleAr,
    string RfqTitleEn,
    string ProposalCode,
    /// <summary>The proposal's own state - Awarded, NotSelected, Declined or AwardOffered - rather
    /// than a derived word, so the chip on this widget and the chip on the bid say the same thing.</summary>
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
    bool ErpDegraded,
    /// <summary>T-039/FEAT-16.3. Empty when this supplier has no resolved bid yet, which is a state
    /// the screen renders rather than hides - a new supplier should see the widget that will hold
    /// their results.</summary>
    IReadOnlyList<DashboardAwardDto> Awards);

public interface ISupplierDashboardHandler
{
    Task<SupplierDashboardDto?> HandleAsync(CancellationToken ct);
}
