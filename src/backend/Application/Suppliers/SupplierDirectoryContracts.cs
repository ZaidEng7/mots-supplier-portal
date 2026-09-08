using System.Text;
using MotsSupplierPortal.Application.Common;

namespace MotsSupplierPortal.Application.Suppliers;

/// <summary>
/// SCR-402, the procurement directory: who can supply what, browsable before an invitation is sent.
///
/// <para><b>Identity and categories, and nothing else.</b> A buyer choosing whom to invite needs to know
/// the company exists, what it sells and that it is currently able to trade. Contact details, bank
/// details and document history are the reviewer's business and the supplier's own - so they are not on
/// this read at all, rather than present and ignored by the screen.</para>
///
/// <para><b>Why lifecycle state is here and onboarding state is not.</b> The directory serves only
/// suppliers whose onboarding is Approved, so the onboarding column would read "Approved" on every row.
/// The lifecycle state does vary and it decides whether an invitation is worth sending: a Suspended
/// supplier is listed and visibly not invitable, because hiding them produces the other defect - a buyer
/// who cannot find a company they know is registered, and no explanation anywhere.</para>
/// </summary>
public sealed record SupplierDirectoryItemDto(
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string LifecycleState,
    IReadOnlyList<string> CategoryCodes,
    int OfferingCount,
    string? City,
    string? RegionCode);

/// <summary>
/// SCR-307, the compliance directory: every supplier, with the document health a reviewer acts on.
///
/// <para><b>Not the review queue.</b> The queue answers "what is waiting for me" and drops a case the
/// moment it is decided - which was F-6, found by a reviewer who wanted to look at a decision they had
/// already made. This answers the other question: what is the state of the registry. Every onboarding
/// state including the decided ones, and the document health of live suppliers, whose certificates expire
/// long after anybody last looked at their application.</para>
/// </summary>
/// <param name="ExpiredDocumentCount">Approved documents whose expiry date has passed and which the
/// expiry job has moved to Expired. On an award-critical type this is also why a supplier is suspended
/// (BRULE-023, live since D-58).</param>
/// <param name="ExpiringDocumentCount">Documents inside the configured renewal window - a prompt, not a
/// problem, and deliberately counted separately so the two are never added together.</param>
/// <param name="RejectedDocumentCount">A reviewer's own decision, still unreplaced.</param>
public sealed record ComplianceDirectoryItemDto(
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string OnboardingState,
    string LifecycleState,
    DateTimeOffset CreatedAt,
    int ExpiredDocumentCount,
    int ExpiringDocumentCount,
    int RejectedDocumentCount);

/// <summary>
/// The accepted filter values for both directories, exposed on the contract because the endpoint has to
/// refuse an unrecognised one and the handler has to map an accepted one.
///
/// <para>Two copies of this vocabulary drift into the gap §6.2 describes: a value the endpoint accepts
/// and the handler does not recognise applies no predicate at all, and an unfiltered list that reads as a
/// filtered one is worse than an error. The review queue's own filter values carry the same note for the
/// same reason.</para>
/// </summary>
public static class SupplierDirectoryFilterValues
{
    /// <summary>Lifecycle states a buyer may filter the directory by. Onboarding state is not filterable:
    /// the directory is Approved-only by construction.</summary>
    public static readonly IReadOnlySet<string> LifecycleStates =
        new HashSet<string>(StringComparer.Ordinal) { "Active", "Suspended" };

    /// <summary>Onboarding states the compliance view may be filtered by - the whole enum this time,
    /// because a reviewer's question includes "who was rejected".</summary>
    public static readonly IReadOnlySet<string> OnboardingStates =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "None", "Draft", "Submitted", "UnderReview", "InfoRequested", "Approved", "Rejected",
        };

    /// <summary>
    /// <c>?documentHealth=</c>: "attention" is any supplier holding an expired, expiring or rejected
    /// document; "ok" is the complement.
    ///
    /// <para>One word rather than three separate booleans because the reviewer's question is one
    /// question - who needs looking at - and three independent flags would let a caller ask for a
    /// combination nobody means, such as expiring-but-not-expired.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> DocumentHealth =
        new HashSet<string>(StringComparer.Ordinal) { "attention", "ok" };
}

/// <summary>
/// Keyset cursor for both directories, ordered by display name with an Id tie-break.
///
/// <para>A name rather than a timestamp, because a directory is read alphabetically - paging by creation
/// date would give a buyer looking for "Al-Sham Trading" no idea which page to ask for. The Id tie-break
/// is what keeps the page boundary stable when two suppliers share a display name, which the schema
/// permits.</para>
///
/// <para>An unparseable token yields the first page rather than an error, matching every other cursor in
/// this codebase (AuditCursor, SessionCursor, ReviewQueueCursor, SupplierUserCursor, RfqListCursor). The
/// property that matters is asserted in the tests: a hostile token never reaches the database.</para>
/// </summary>
public readonly record struct SupplierDirectoryCursor(string Name, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Id:N}:{Name}"));

    public static bool TryDecode(string? value, out SupplierDirectoryCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        Span<byte> buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written)) return false;

        // The id first and the name last, split once: a display name may contain a colon, and an
        // id may not.
        var decoded = Encoding.UTF8.GetString(buffer[..written]);
        var separator = decoded.IndexOf(':');
        if (separator <= 0 || !Guid.TryParseExact(decoded[..separator], "N", out var id)) return false;

        cursor = new SupplierDirectoryCursor(decoded[(separator + 1)..], id);
        return true;
    }
}

public interface IListSupplierDirectoryHandler
{
    /// <summary>SCR-402: approved suppliers, alphabetically, filterable by category and lifecycle state
    /// and searchable by name or supplier code.</summary>
    Task<ListEnvelope<SupplierDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? category, string? lifecycleState, string? q,
        CancellationToken ct);
}

public interface IListComplianceDirectoryHandler
{
    /// <summary>SCR-307: every supplier with its onboarding state and document health.</summary>
    Task<ListEnvelope<ComplianceDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? onboardingState, string? documentHealth, string? q,
        CancellationToken ct);
}
