// The vocabulary for the screen where an administrator rewords a transactional email, and the placeholders
// their wording must keep.
//
//
// WHY THE PLACEHOLDERS ARE HARDER HERE THAN FOR IN-APP WORDING
//
// An in-app notification that loses a placeholder reads badly. An email that loses its verification link
// locks the recipient out of the account they are trying to create, and nothing in the system can tell that
// it happened: the send succeeded, the body was valid, and the applicant simply never gets in.
//
// So the contract is declared per template and enforced when the wording is written rather than when the
// email is sent.
//
// The required placeholders must all appear. That list is empty for templates that carry no link, such as an
// approval notice, which tells the recipient something and asks nothing of them.
//
// The optional ones may appear. Anything outside both lists is refused, because a placeholder the system has
// no value for reaches the recipient as literal characters and cannot be diagnosed from the sent mail.

namespace MotsSupplierPortal.Application.Admin;

public sealed record EmailTemplateDefinition(
    string Key,
    IReadOnlyList<string> RequiredTokens,
    IReadOnlyList<string> OptionalTokens);

public static class EmailTemplateKeys
{
    public const string Verification = "email.verification";
    public const string PasswordReset = "email.password_reset";
    public const string SupplierUserInvite = "email.supplier_user_invite";
    public const string StaffInvite = "email.staff_invite";
    public const string AlreadyRegisteredNotice = "email.already_registered";
    public const string ApplicationApproved = "email.application_approved";
    public const string ApplicationRejected = "email.application_rejected";
    public const string InfoRequested = "email.info_requested";
    public const string ApplicationResubmitted = "email.application_resubmitted";
    public const string RfqInvitation = "email.rfq_invitation";
    public const string ClarificationAnswered = "email.clarification_answered";
    public const string ClarificationPublished = "email.clarification_published";
    public const string ClarificationPosted = "email.clarification_posted";
    public const string RfqAddendum = "email.rfq_addendum";
    public const string RfqPublished = "email.rfq_published";
    public const string RfqCancelled = "email.rfq_cancelled";
    public const string ProposalSubmitted = "email.proposal_submitted";
    public const string EvaluatorAssigned = "email.evaluator_assigned";
    public const string AwardIssued = "email.award_issued";
    public const string AwardRegret = "email.award_regret";
    public const string DocumentRejected = "email.document_rejected";
    public const string DocumentExpiring = "email.document_expiring";
    public const string DocumentExpired = "email.document_expired";

    public static readonly EmailTemplateDefinition[] All =
    [
        new(Verification, ["verifyUrl"], []),
        new(PasswordReset, ["resetUrl"], []),
        new(SupplierUserInvite, ["acceptUrl"], []),
        new(StaffInvite, ["acceptUrl"], []),
        new(AlreadyRegisteredNotice, [], ["publicUrl"]),
        new(ApplicationApproved, [], []),
        new(ApplicationRejected, [], ["reason"]),
        new(InfoRequested, [], ["reason"]),
        new(ApplicationResubmitted, [], ["referenceCode"]),
        new(RfqInvitation, ["deepLink"], ["referenceCode", "rfqTitle"]),
        new(ClarificationAnswered, [], ["referenceCode"]),
        new(ClarificationPublished, [], ["referenceCode"]),
        new(ClarificationPosted, [], ["referenceCode"]),
        new(RfqAddendum, [], ["referenceCode", "addendumTitle"]),
        new(RfqPublished, [], ["referenceCode"]),
        new(RfqCancelled, [], ["referenceCode"]),
        new(ProposalSubmitted, [], ["proposalReferenceCode", "rfqReferenceCode"]),
        new(EvaluatorAssigned, [], ["referenceCode"]),
        new(AwardIssued, [], ["rfqReferenceCode"]),
        new(AwardRegret, [], ["rfqReferenceCode"]),
        new(DocumentRejected, [], ["fileName", "reason"]),
        new(DocumentExpiring, [], ["fileName"]),
        new(DocumentExpired, [], ["fileName"]),
    ];

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}

public sealed record EmailTemplateOverrideDto(
    string Key,
    string SubjectAr,
    string SubjectEn,
    string BodyAr,
    string BodyEn,
    DateTimeOffset UpdatedAt);

public sealed record EmailTemplateRowDto(
    string Key,
    IReadOnlyList<string> RequiredTokens,
    IReadOnlyList<string> OptionalTokens,
    EmailTemplateOverrideDto? Override,
    EmailTemplateOverrideDto Shipped);

public sealed record UpsertEmailTemplateCommand(
    string Key, string SubjectAr, string SubjectEn, string BodyAr, string BodyEn, Guid ActorUserId);

public abstract record UpsertEmailTemplateResult
{
    public sealed record Success(EmailTemplateOverrideDto Override) : UpsertEmailTemplateResult;
    public sealed record UnknownKey : UpsertEmailTemplateResult;

    public sealed record MissingRequiredTokens(IReadOnlyList<string> MissingTokens) : UpsertEmailTemplateResult;

    public sealed record UnknownTokens(IReadOnlyList<string> Tokens) : UpsertEmailTemplateResult;
}

public interface IListEmailTemplatesHandler
{
    Task<IReadOnlyList<EmailTemplateRowDto>> HandleAsync(CancellationToken ct);
}

public interface IUpsertEmailTemplateHandler
{
    Task<UpsertEmailTemplateResult> HandleAsync(UpsertEmailTemplateCommand command, CancellationToken ct);
}

public interface IDeleteEmailTemplateHandler
{
    Task<bool> HandleAsync(string key, CancellationToken ct);
}

public interface IEmailCopySource
{
    Task<(string Subject, string Body)> ComposeAsync(
        string key,
        string? locale,
        IReadOnlyDictionary<string, string> tokens,
        Func<(string Subject, string Body)> shipped,
        CancellationToken ct);
}
