using System.Buffers.Binary;

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
    /// <summary>The quoted entity-tag for a row version, ready to put in a header.</summary>
    public static string Format(uint rowVersion)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, rowVersion);
        return $"\"{Base64Url(bytes)}\"";
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
    public static bool TryParse(string? headerValue, out uint rowVersion)
    {
        rowVersion = 0;
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        var value = headerValue.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        value = value.Trim('"');
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
