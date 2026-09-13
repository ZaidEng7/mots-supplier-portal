// The list of error type identifiers this API can put on a failure response, transcribed from the
// written interface contract.
//
// Every non-success response carries a type, a URL naming what kind of failure it was. These are
// constants rather than strings typed at each call site, for the same reason the review queue's
// filter words moved onto a contract: a slug spelled by hand can drift from the document by one
// character and nobody notices, because nothing compares the two. All makes the set enumerable, so a
// test can assert every slug the code can emit is one the document defines.
//
// The document calls its own table an extract, so the set is not closed. But a case with no row in it
// is a documentation gap to report, not permission to invent a slug. Where a case has no row, the
// closest documented slug is reused and the divergence reported. UnknownFilter is one such: the
// document names it outside the main table. An unknown sort key is another, where the document
// requires a refusal and names no slug, so it reuses the validation one.
//
// ForStatus is the fallback mapping for a response that carries no slug of its own. The document
// pairs each slug with a status, so this is transcription rather than judgement, with one exception:
// status 409 has two rows, an illegal state change and a duplicate resource, and a bare 409 cannot
// be told apart. The more general one is the default, and a handler that means the state-change case
// says so explicitly. Recorded as a documented ambiguity rather than guessed at each call site.

namespace MotsSupplierPortal.Api.Errors;

public static class ProblemTypes
{
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

    public const string UnknownFilter = Base + "unknown-filter";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Validation, MalformedRequest, Unauthorized, Forbidden, NotFound, InvalidStateTransition,
        Conflict, PreconditionFailed, PreconditionRequired, IdempotencyConflict, RateLimited,
        PayloadTooLarge, UnsupportedMediaType, DependencyUnavailable, Internal, UnknownFilter,
    };

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
