using System.Buffers.Binary;
using System.Reflection;

namespace MotsSupplierPortal.Api.Concurrency;

/// <summary>
/// §8.1's wire format for a row version: "the current <c>RowVersion</c>, base64url".
///
/// <para><b>Strong, not weak, and the document contradicts itself on this.</b> §8.1 says the version
/// "surfaces on the wire as a <b>strong</b> ETag" and then prints the example as
/// <c>ETag: "W/…"</c> - but <c>W/</c> is HTTP's weak-validator prefix, so the sentence and the
/// example cannot both be followed. The sentence wins, because RFC 9110 §13.1.1 requires
/// <b>strong</b> comparison for <c>If-Match</c>: a weak ETag is not usable as a precondition at all,
/// and emitting <c>W/</c> would make the rest of §8.1 - the 428, the 412, the whole lost-update
/// guard - non-functional by construction. Recorded as a documented conflict rather than resolved
/// silently.</para>
///
/// <para>Base64url of the four big-endian bytes of the <c>uint</c>. Big-endian so the encoded form
/// sorts the way the number does, which matters only to a human reading two of them side by side in
/// a log, and costs nothing.</para>
/// </summary>
public static class ETag
{
    /// <summary>
    /// A discriminator for THIS BUILD, appended to every entity-tag.
    ///
    /// <para><b>Why an entity-tag needs to know the build.</b> The tag encoded the aggregate's row version and
    /// nothing else, so it identified the DATA and not the response. Adding a field to a DTO does not change
    /// any row version - so after a deploy, every client holding a cached body for an unchanged row kept that
    /// body, and the new field was invisible to it. Found while verifying a one-line addition to the
    /// consolidated-results table: the API returned the new field to curl and the browser rendered the old
    /// shape, because its cached body still matched the tag.</para>
    ///
    /// <para>That is not a browser quirk; it is what the caching contract says to do. §8.1 asks the tag to
    /// identify the representation, and the representation includes its shape.</para>
    ///
    /// <para>Derived from the assembly's informational version, which CI stamps with the commit - so it changes
    /// exactly once per deployment and is stable across every request of one. Truncated to eight characters
    /// because it only has to differ, not to be recoverable.</para>
    /// </summary>
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

    /// <summary>
    /// The quoted entity-tag for a row version, ready to put in a header.
    ///
    /// <para>Shaped <c>"&lt;version&gt;.&lt;build&gt;"</c>. The version half is what <see cref="TryParse"/>
    /// reads back, so <c>If-Match</c> keeps working exactly as before; the build half is what makes a cached
    /// body from an older deployment stop matching.</para>
    /// </summary>
    public static string Format(uint rowVersion, string resourceKey)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return $"\"{Base64Url(bytes)}.{BuildTag}.{Discriminator(resourceKey)}\"";
    }

    /// <summary>
    /// A tag asserting a ROW VERSION and nothing else, for a caller sending <c>If-Match</c>.
    ///
    /// <para>Separate from <see cref="Format(uint, string)"/> because the two headers ask different
    /// questions. <c>If-Match</c> asks "am I writing over the row I read", which <see cref="TryParse"/>
    /// answers from the version half alone - so a precondition never needs the resource discriminator, and
    /// tests that construct one should not have to invent a resource key to say "version 4".</para>
    /// </summary>
    public static string ForPrecondition(uint rowVersion)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return $"\"{Base64Url(bytes)}.{BuildTag}\"";
    }

    /// <summary>
    /// Eight hex characters identifying WHICH resource a tag belongs to.
    ///
    /// <para><b>Why a tag needs this.</b> The tag encoded a row version and a build and nothing else, so
    /// every resource sitting at version 3 carried the byte-identical tag <c>"AAAAAw.&lt;build&gt;"</c> -
    /// one supplier's profile, another supplier's profile, an unrelated tender. An entity-tag is supposed
    /// to identify a representation (RFC 9110 §8.8.3); this one identified a number.</para>
    ///
    /// <para>What that cost: two suppliers signing into the same browser share one cache entry for
    /// <c>/api/v1/suppliers/me</c>. The second one's conditional read sent the first one's tag, it matched
    /// the second one's own row version, the server answered 304 Not Modified, and the browser served the
    /// FIRST supplier's body - legal name, registration number and an Approved onboarding state that
    /// belonged to someone else. Reported by the person it happened to.</para>
    ///
    /// <para>The key passed in is the request path plus the authenticated subject, so the tag is specific
    /// to one resource as seen by one caller. Truncated to eight characters because it only has to differ:
    /// it is a discriminator inside a validator, not a secret and not a checksum of the body.</para>
    /// </summary>
    private static string Discriminator(string resourceKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resourceKey));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <summary>
    /// Reads a row version back out of an <c>If-Match</c> or <c>If-None-Match</c> value.
    ///
    /// <para>Tolerant of the quoting and of a <c>W/</c> prefix a client might send back after
    /// reading the document's example, because rejecting a caller over punctuation would turn a
    /// working guard into a 412 nobody can diagnose. Not tolerant of a bare decimal, which is what
    /// this header carried before §8.1 was implemented - that format is gone, and accepting it
    /// would leave two wire formats alive at once.</para>
    /// </summary>
    /// <summary>
    /// Whether a caller's <c>If-None-Match</c> candidate identifies the representation this build would send -
    /// both the row version AND the build.
    ///
    /// <para><b>Stricter than <see cref="TryParse"/>, deliberately, and the two are answering different
    /// questions.</b> <c>If-Match</c> asks "am I writing over the row I read", which is about the DATA, so a tag
    /// from an older build is a valid claim and must be accepted. <c>If-None-Match</c> asks "is the body I
    /// already have still correct", which is about the REPRESENTATION - and a body from a build whose DTOs had
    /// fewer fields is not, even though the row has not moved.</para>
    ///
    /// <para>Requiring only the version is what made the bug: a field added to a DTO changed no row version, so
    /// a warm client kept its old body and the new field was invisible to it. Reproduced with curl against a
    /// running server - a matching <c>If-None-Match</c> answered 304 while a plain read returned the new
    /// field.</para>
    /// </summary>
    public static bool MatchesCurrentRepresentation(string? candidate, uint rowVersion, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;

        var value = candidate.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');

        // Whole-string equality against the tag this request would emit, rather than three separate
        // comparisons. A candidate missing any half - a tag from before the build discriminator existed, or
        // from before the resource discriminator did - simply is not equal, which is the right answer for
        // both: each of those older shapes is exactly the stale-body case the comparison exists to refuse.
        return string.Equals(value, Format(rowVersion, resourceKey).Trim('"'), StringComparison.Ordinal);
    }

    public static bool TryParse(string? headerValue, out uint rowVersion)
    {
        rowVersion = 0;
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        var value = headerValue.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');

        // Everything from the first '.' is discriminator - the build, and now the resource - see Format.
        // Dropped here so a tag issued by any build still yields the row version a caller is asserting: a
        // client that read a version before a deploy and writes after it is making a legitimate claim about
        // the row, and refusing it over a suffix would turn a working guard into a 412 nobody can explain.
        //
        // This is about If-Match, which asks "am I writing over the row I read" - a question about the DATA.
        // The resource half is therefore NOT enforced here, and that is a deliberate line rather than an
        // oversight: the handler has already loaded the row the path names, so the version is compared
        // against that row and not against whatever the tag came from.
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
