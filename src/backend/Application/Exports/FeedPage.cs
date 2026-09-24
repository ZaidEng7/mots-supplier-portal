// How large a page of a feed may be.
//
// THESE ARE NOT THE LIST SIZES THE SCREENS USE, and the difference is deliberate rather than an oversight.
// ListEnvelope defaults to twenty rows and caps at a hundred, which is right for something a person scrolls.
// The ministry's requirements ask for at least five hundred rows per page, because the caller is a nightly job
// whose cost is dominated by round trips rather than by the size of each answer. Reusing the screen's constants
// would have quietly failed a Must while looking tidy.
//
// FIVE HUNDRED IS THE DEFAULT, which is their floor rather than our guess: a caller that says nothing gets the
// page size their own document specifies.
//
// TWO THOUSAND IS THE CEILING. It is not a performance limit - it is the point past which one answer stops
// being a page and becomes the whole feed again, at which point the caller should be streaming the CSV. A
// request for more is clamped rather than refused, because a loader asking for ten thousand rows is asking for
// something reasonable in a way we cannot give it, and an error would leave it with nothing.
//
// A NONSENSE LIMIT BECOMES THE DEFAULT rather than an error, for the same reason. Zero and negative page sizes
// come from a caller's arithmetic, not from a decision, and answering an empty page forever is the worst of the
// available responses: it looks like a feed that has no data.

namespace MotsSupplierPortal.Application.Exports;

public static class FeedPage
{
    public const int DefaultLimit = 500;

    public const int MaxLimit = 2000;

    public static int ClampLimit(int? requested) =>
        requested is null or < 1 ? DefaultLimit : Math.Min(requested.Value, MaxLimit);
}
