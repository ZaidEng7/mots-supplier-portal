// The two supplier directories, and the filter values both accept.
//
//
// THE BUYER'S DIRECTORY: WHO CAN SUPPLY WHAT
//
// Identity and categories, and nothing else. A buyer choosing whom to invite needs to know the company exists,
// what it sells, and whether it can currently trade.
//
// Contact details, bank details and document history are the reviewer's business and the supplier's own, so
// they are not on this read at all rather than present and ignored by the screen.
//
// The lifecycle state is here and the registration state is not. This directory serves only suppliers whose
// registration was approved, so a registration column would read the same on every row. The lifecycle state
// does vary, and it decides whether an invitation is worth sending.
//
//
// THE REVIEWER'S DIRECTORY: THE STATE OF THE REGISTRY
//
// It is not the review queue. The queue answers what is waiting for me, and drops a case the moment it is
// decided, which was found by a reviewer who wanted to look again at a decision they had already made.
//
// This answers the other question. Every registration state including the decided ones, and the document
// health of live suppliers, whose certificates expire long after anybody last looked at their application.
//
// The three document counts are kept apart deliberately. Expired documents are a problem and, on an
// award-critical type, are also why a supplier is suspended. Documents inside the renewal window are a prompt
// rather than a problem. A rejected document is a reviewer's own decision, still unreplaced. Adding any of them
// together would lose the distinction that decides what to do.
//
//
// THE FILTER VALUES ARE ON THE CONTRACT
//
// Because the route has to refuse an unrecognised value and the handler has to map an accepted one, and two
// copies of that vocabulary drift into exactly the gap the rules describe: a value the route accepts and the
// handler quietly ignores, which returns an unfiltered list that reads as a filtered one.

namespace MotsSupplierPortal.Application.Suppliers;

using System.Text;
using MotsSupplierPortal.Application.Common;

public sealed record SupplierDirectoryItemDto(
    string SupplierCode,
    string DisplayNameAr,
    string DisplayNameEn,
    string LifecycleState,
    IReadOnlyList<string> CategoryCodes,
    int OfferingCount,
    string? City,
    string? RegionCode);

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

public static class SupplierDirectoryFilterValues
{
    public static readonly IReadOnlySet<string> LifecycleStates =
        new HashSet<string>(StringComparer.Ordinal) { "Active", "Suspended" };

    public static readonly IReadOnlySet<string> OnboardingStates =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "None", "Draft", "Submitted", "UnderReview", "InfoRequested", "Approved", "Rejected",
        };

    public static readonly IReadOnlySet<string> DocumentHealth =
        new HashSet<string>(StringComparer.Ordinal) { "attention", "ok" };
}

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

        var decoded = Encoding.UTF8.GetString(buffer[..written]);
        var separator = decoded.IndexOf(':');
        if (separator <= 0 || !Guid.TryParseExact(decoded[..separator], "N", out var id)) return false;

        cursor = new SupplierDirectoryCursor(decoded[(separator + 1)..], id);
        return true;
    }
}

public interface IListSupplierDirectoryHandler
{
    Task<ListEnvelope<SupplierDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? category, string? lifecycleState, string? q,
        CancellationToken ct);
}

public interface IListComplianceDirectoryHandler
{
    Task<ListEnvelope<ComplianceDirectoryItemDto>> HandleAsync(
        string? cursor, int? limit, bool withCount, string? onboardingState, string? documentHealth, string? q,
        CancellationToken ct);
}
