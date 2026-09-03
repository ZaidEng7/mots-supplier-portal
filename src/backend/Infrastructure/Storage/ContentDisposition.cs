using System.Text;

namespace MotsSupplierPortal.Infrastructure.Storage;

/// <summary>
/// Builds an RFC 6266 <c>Content-Disposition</c> header for a download.
///
/// <para><b>The defect this replaces.</b> The header was assembled as
/// <c>$"attachment; filename=\"{name}\""</c> with the caller's file name interpolated raw. A name
/// containing a double quote closes the quoted-string early and everything after it is parsed as
/// further header parameters; a name containing CR or LF ends the header line entirely and whatever
/// follows becomes a header of its own. Both are header injection, and the file name is
/// attacker-supplied - it is whatever the uploader typed.</para>
///
/// <para><b>Why not just strip non-ASCII.</b> The obvious fix - escape to ASCII and drop the rest -
/// would destroy every Arabic file name in this product, which is most of them. That is a regression
/// wearing a fix's clothes. RFC 6266 already solves it: emit BOTH parameters, an ASCII-safe
/// <c>filename</c> for old clients and a percent-encoded <c>filename*</c> carrying the real UTF-8
/// name, which every current browser prefers.</para>
/// </summary>
public static class ContentDisposition
{
    /// <summary>Fallback used when a name has nothing left after ASCII sanitisation - an
    /// all-Arabic name, for instance. Never an empty <c>filename</c>, which some clients treat as
    /// "no name" and save as the URL's last segment - here, a bare GUID.</summary>
    private const string AsciiFallback = "download";

    public static string Attachment(string fileName)
    {
        var ascii = ToAsciiFallback(fileName);
        var encoded = PercentEncode(fileName);

        // filename* wins wherever both are understood (RFC 6266 §4.3), so the ASCII form is only
        // ever seen by a client that could not have rendered the real name anyway.
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
    }

    /// <summary>
    /// The quoted-string form: an ALLOW-LIST of characters that cannot restructure the header.
    ///
    /// <para><b>Written as a deny-list first, and that was wrong.</b> Removing only the quote and
    /// backslash left the semicolon, so <c>in"jected"; x=y.pdf</c> sanitised to
    /// <c>injected; x=y.pdf</c> - which still closes the parameter and injects another one. The
    /// unit test caught it. A deny-list has to anticipate every separator the grammar gives meaning
    /// to; an allow-list only has to name what is safe, and a file name that loses an unusual
    /// character in a FALLBACK parameter costs nothing, because any client that can render it reads
    /// <c>filename*</c> instead.</para>
    ///
    /// <para>Control characters are outside the allowed range by construction, which is what closes
    /// the header-splitting half of the defect.</para>
    /// </summary>
    private static string ToAsciiFallback(string fileName)
    {
        var builder = new StringBuilder(fileName.Length);

        foreach (var c in fileName)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '-' or '_' or '(' or ')')
            {
                builder.Append(c);
            }
        }

        var ascii = builder.ToString().Trim();
        return ascii.Length == 0 ? AsciiFallback : ascii;
    }

    /// <summary>
    /// RFC 5987 percent-encoding of the UTF-8 bytes: only the unreserved set survives unescaped, so
    /// Arabic, spaces, quotes and CRLF alike become %XX and cannot affect the header's structure.
    /// </summary>
    private static string PercentEncode(string fileName)
    {
        var builder = new StringBuilder(fileName.Length * 3);

        foreach (var b in Encoding.UTF8.GetBytes(fileName))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '&' or '+' or '-' or '.'
                or '^' or '_' or '`' or '|' or '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
