namespace MotsSupplierPortal.Api.Errors;

/// <summary>
/// API-ARCHITECTURE.md §7.1's canonical <c>type</c> slug catalogue, transcribed exactly.
///
/// <para><b>Typed rather than string literals at call sites</b>, for the same reason the review
/// queue's filter vocabulary moved onto the contract: a slug spelled by hand at a call site is a
/// slug that can drift from the document by one character and never be noticed, because nothing
/// compares the two. <see cref="All"/> makes the set enumerable so a test can assert that every
/// slug the code can emit is one §7.1 documents.</para>
///
/// <para><b>§7.1 calls itself an "(extract)"</b>, so it is not closed - but a case with no row is a
/// documentation gap to report, not a licence to invent a slug. Where a case has no row, the
/// closest documented slug is reused and the divergence reported, the same choice made for
/// <c>UNKNOWN_SORT_KEY</c> (§6.3 requires a 422 and names no slug, so it reuses
/// <see cref="Validation"/>).</para>
/// </summary>
public static class ProblemTypes
{
    /// <summary>§7's worked example gives the absolute form; every slug below is that base + its row.</summary>
    public const string Base = "https://api.mots-portal.sy/errors/";

    public const string Validation = Base + "validation";
    public const string MalformedRequest = Base + "malformed-request";
    public const string Unauthorized = Base + "unauthorized";
    public const string Forbidden = Base + "forbidden";
    public const string NotFound = Base + "not-found";
    public const string InvalidStateTransition = Base + "invalid-state-transition";
    public const string Conflict = Base + "conflict";
    public const string PreconditionFailed = Base + "precondition-failed";
    public const string PreconditionRequired = Base + "precondition-required";
    public const string IdempotencyConflict = Base + "idempotency-conflict";
    public const string RateLimited = Base + "rate-limited";
    public const string PayloadTooLarge = Base + "payload-too-large";
    public const string UnsupportedMediaType = Base + "unsupported-media-type";
    public const string DependencyUnavailable = Base + "dependency-unavailable";
    public const string Internal = Base + "internal";

    /// <summary>§6.2 names this one outside the §7.1 table: "Unknown filter key → 422
    /// (`type: …/errors/unknown-filter`)". Documented, just not in the catalogue.</summary>
    public const string UnknownFilter = Base + "unknown-filter";

    /// <summary>Every slug this codebase may emit. The coverage test asserts nothing outside it.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Validation, MalformedRequest, Unauthorized, Forbidden, NotFound, InvalidStateTransition,
        Conflict, PreconditionFailed, PreconditionRequired, IdempotencyConflict, RateLimited,
        PayloadTooLarge, UnsupportedMediaType, DependencyUnavailable, Internal, UnknownFilter,
    };

    /// <summary>
    /// The status → slug mapping for responses that carry no slug of their own.
    ///
    /// <para>§7.1 pairs each slug with an HTTP status, so this is transcription rather than
    /// judgement - except where one status has two rows. 409 is both
    /// <c>invalid-state-transition</c> (ILLEGAL_TRANSITION) and <c>conflict</c>
    /// (DUPLICATE_RESOURCE), and a bare 409 cannot be told apart, so the more general
    /// <see cref="Conflict"/> is the default and a handler that means the transition case says so
    /// explicitly. Reported as a documented ambiguity rather than guessed per call site.</para>
    /// </summary>
    public static string ForStatus(int status) => status switch
    {
        400 => MalformedRequest,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        412 => PreconditionFailed,
        413 => PayloadTooLarge,
        415 => UnsupportedMediaType,
        422 => Validation,
        428 => PreconditionRequired,
        429 => RateLimited,
        503 => DependencyUnavailable,
        _ => Internal,
    };
}
