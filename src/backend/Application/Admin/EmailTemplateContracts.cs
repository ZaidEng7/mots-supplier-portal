namespace MotsSupplierPortal.Application.Admin;

/// <summary>
/// T-076. One email template an administrator may reword, and the tokens their wording MUST keep.
///
/// <para><b>Required tokens are the whole reason this is harder than T-061's in-app copy.</b> An in-app
/// notification that loses a token reads badly. An email that loses <c>{verifyUrl}</c> locks the recipient
/// out of the account they are trying to create, and nothing in the system can tell that it happened - the
/// send succeeded, the body was valid HTML, and the applicant simply never gets in. So the contract is
/// per template and enforced on the write, not on the send.</para>
/// </summary>
/// <param name="RequiredTokens">Must all appear in the override's body. Empty for templates that carry no
/// link - an approval notice, for instance, tells the recipient something and asks nothing of them.</param>
/// <param name="OptionalTokens">May appear. A token outside Required ∪ Optional is refused, for D-34's
/// reason: a token the payload cannot fill reaches the recipient as the literal characters and cannot be
/// diagnosed from the sent mail.</param>
public sealed record EmailTemplateDefinition(
    string Key,
    IReadOnlyList<string> RequiredTokens,
    IReadOnlyList<string> OptionalTokens);

/// <summary>
/// The keys, and the token contract for each. Named constants rather than an enum because the value is
/// persisted as text and read by an administrator on a screen: renaming one must be a visible change to a
/// string that already exists in rows.
/// </summary>
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

    /// <summary>
    /// Every template, with what its payload can fill.
    ///
    /// <para>The four with a REQUIRED token are the four whose omission is a lockout rather than a
    /// readability problem: three single-use links and the RFQ deep link. Everything else names its tokens
    /// as optional, because a reworded body that drops a reference code is worse copy and not a broken
    /// journey - and refusing it would stop an administrator writing "your application was approved"
    /// without repeating the code in the sentence.</para>
    /// </summary>
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
        // The deep link is required: an invitation a supplier cannot open is an invitation they will miss,
        // which USER-JOURNEYS names as the legacy system's single worst failure.
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
        new(DocumentRejected, [], ["documentTypeName", "reason"]),
        new(DocumentExpiring, [], ["documentTypeName"]),
        new(DocumentExpired, [], ["documentTypeName"]),
    ];

    public static EmailTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));
}

/// <summary>An administrator's rewording of one email template. Absent means the shipped copy.</summary>
public sealed record EmailTemplateOverrideDto(
    string Key,
    string SubjectAr,
    string SubjectEn,
    string BodyAr,
    string BodyEn,
    DateTimeOffset UpdatedAt);

/// <param name="Shipped">The copy this override replaces, rendered with the tokens left in place. On the
/// DTO because a screen that cannot show what it is replacing makes revert guesswork - the same reason
/// SCR-715 shows it.</param>
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

    /// <param name="MissingTokens">Named, per locale, because "a token is missing" is not actionable and
    /// "the Arabic body no longer contains {verifyUrl}" is.</param>
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
    /// <summary>False when there was nothing to remove. Deleting restores the shipped copy.</summary>
    Task<bool> HandleAsync(string key, CancellationToken ct);
}

/// <summary>
/// What the send path uses. Returns the administrator's wording when there is one and the shipped copy
/// otherwise, with the caller's tokens interpolated either way.
/// </summary>
public interface IEmailCopySource
{
    Task<(string Subject, string Body)> ComposeAsync(
        string key,
        string? locale,
        IReadOnlyDictionary<string, string> tokens,
        Func<(string Subject, string Body)> shipped,
        CancellationToken ct);
}
