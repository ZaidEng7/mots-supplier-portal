// The entity tag: how a record's version travels on the wire, so two people cannot silently overwrite
// each other and a browser can ask whether the copy it already has is still good.
//
// A tag looks like a version, a build marker and a resource marker, quoted. It is a strong tag rather
// than a weak one, and the written contract contradicts itself on that point: the sentence says strong
// and the example prints the weak prefix. The sentence wins, because the standard requires strong
// comparison for a write precondition. A weak tag cannot be used as a precondition at all, so emitting
// one would make the rest of the contract, the refusals and the whole lost-update guard, non-functional
// by construction. Recorded as a documented conflict rather than resolved silently.
//
// The version is encoded most-significant-byte first, so the encoded form sorts the way the number
// does. That matters only to a person reading two of them side by side in a log, and it costs nothing.
//
//
// THE BUILD MARKER, AND WHY A TAG HAS TO KNOW THE BUILD
//
// The tag used to encode the record's version and nothing else, so it identified the data rather than
// the response. Adding a field to a response shape changes no version, so after a deployment every
// client holding a cached body for an unchanged record kept that body, and the new field was invisible
// to it.
//
// Found while verifying a one-line addition to the consolidated results table: the API returned the new
// field to a command-line request and the browser rendered the old shape, because its cached body still
// matched the tag. That is not a browser quirk; it is what the caching standard says to do. The tag is
// meant to identify the representation, and the representation includes its shape.
//
// The marker is derived from the build's own version string, which the build pipeline stamps with the
// commit, so it changes exactly once per deployment and is identical across every request of one. Eight
// characters, because it only has to differ, not to be recoverable.
//
//
// THE RESOURCE MARKER, AND WHAT ITS ABSENCE COST
//
// With only a version and a build, every resource sitting at version three carried a byte-identical
// tag: one supplier's profile, another supplier's profile, an unrelated tender. A tag is supposed to
// identify a representation; that one identified a number.
//
// What it cost: two suppliers signing into the same browser share one cache entry for the
// current-supplier route. The second one's conditional read sent the first one's tag, it matched the
// second one's own version, the server answered not-modified, and the browser served the first
// supplier's body: legal name, registration number, and an approved status that belonged to somebody
// else. Reported by the person it happened to.
//
// The key passed in is the request path plus the signed-in subject, so a tag is specific to one
// resource as seen by one caller. Eight characters again, because it only has to differ. It is a
// discriminator inside a validator, not a secret and not a checksum of the body.
//
//
// THE THREE OPERATIONS, WHICH ANSWER THREE DIFFERENT QUESTIONS
//
// Format builds the full tag for a response. The version part is what a write precondition reads back,
// so preconditions keep working exactly as before, and the build part is what makes a cached body from
// an older deployment stop matching.
//
// ForPrecondition builds a tag asserting a version and nothing else. It is separate from Format because
// a write precondition asks "am I writing over the record I read", which is answered from the version
// alone. A precondition never needs the resource marker, and a test constructing one should not have to
// invent a resource key just to say "version four".
//
// MatchesCurrentRepresentation answers a read's question: is the body I already have still correct? That
// is about the representation rather than the data, so it compares the whole string against the tag this
// request would emit, rather than three separate comparisons. A candidate missing any part, one from
// before the build marker existed or before the resource marker did, simply is not equal, which is the
// right answer for both: each of those older shapes is exactly the stale-body case this comparison
// exists to refuse.
//
// Requiring only the version is what made the original bug: a field added to a response changed no
// version, so a warm client kept its old body. Reproduced against a running server, where a matching
// conditional read answered not-modified while a plain read returned the new field.
//
// TryParse reads a version back out of either precondition header. It is tolerant of the quoting and of
// the weak prefix a client might send back after reading the document's example, because refusing a
// caller over punctuation would turn a working guard into a failure nobody can diagnose. It is not
// tolerant of a bare number, which is what this header carried before the current contract; accepting it
// would leave two wire formats alive at once.
//
// TryParse drops everything after the first separator, so a tag issued by any build still yields the
// version a caller is asserting. A client that read a version before a deployment and writes after it is
// making a legitimate claim about the record, and refusing it over a suffix would turn a working guard
// into a failure nobody can explain.
//
// The resource marker is deliberately not enforced there, and that is a line rather than an oversight:
// by that point the handler has already loaded the record the path names, so the version is compared
// against that record rather than against whatever the tag came from.

namespace MotsSupplierPortal.Api.Concurrency;

using System.Buffers.Binary;
using System.Reflection;

public static class ETag
{
    private static readonly string BuildTag = ComputeBuildTag();

    private static string ComputeBuildTag()
    {
        var informational = typeof(ETag).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ETag).Assembly.GetName().Version?.ToString()
            ?? "dev";

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(informational));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public static string Format(uint rowVersion, string resourceKey)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return $"\"{Base64Url(bytes)}.{BuildTag}.{Discriminator(resourceKey)}\"";
    }

    public static string ForPrecondition(uint rowVersion)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return $"\"{Base64Url(bytes)}.{BuildTag}\"";
    }

    private static string Discriminator(string resourceKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resourceKey));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public static bool MatchesCurrentRepresentation(string? candidate, uint rowVersion, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var value = candidate.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');

        return string.Equals(value, Format(rowVersion, resourceKey).Trim('"'), StringComparison.Ordinal);
    }

    public static bool TryParse(string? headerValue, out uint rowVersion)
    {
        rowVersion = 0;
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        var value = headerValue.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');

        var separator = value.IndexOf('.', StringComparison.Ordinal);
        if (separator >= 0) value = value[..separator];

        if (value.Length == 0) return false;

        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!TryFromBase64Url(value, bytes)) return false;

        rowVersion = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryFromBase64Url(string value, Span<byte> destination)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };

        return Convert.TryFromBase64String(padded, destination, out var written) && written == destination.Length;
    }
}
