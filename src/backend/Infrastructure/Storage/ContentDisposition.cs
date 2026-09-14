// Building the download header that names a file.
//
//
// THE DEFECT THIS REPLACES
//
// The header was assembled by interpolating the caller's filename raw.
//
// A name containing a double quote closes the quoted string early and everything after it is parsed as further
// header parameters. A name containing a line break ends the header line entirely and whatever follows becomes a
// header of its own.
//
// Both are header injection, and the filename is attacker-supplied: it is whatever the uploader typed.
//
//
// WHY NOT SIMPLY STRIP NON-ASCII
//
// The obvious fix, escaping to ASCII and dropping the rest, would destroy every Arabic filename in this product,
// which is most of them. That is a regression wearing a fix's clothes.
//
// The standard already solves it: emit BOTH parameters, an ASCII-safe name for old clients and a percent-encoded
// one carrying the real name, which every current browser prefers. The encoded form wins wherever both are
// understood, so the ASCII form is only ever seen by a client that could not have rendered the real name anyway.
//
//
// THE ASCII FORM IS AN ALLOW-LIST, AND IT WAS A DENY-LIST FIRST
//
// Removing only the quote and the backslash left the semicolon, so a crafted name still closed the parameter and
// injected another one. The unit test caught it.
//
// A deny-list has to anticipate every separator the grammar gives meaning to. An allow-list only has to name what
// is safe, and a filename that loses an unusual character in a FALLBACK parameter costs nothing, because any
// client that can render it reads the other one instead.
//
// Control characters are outside the allowed range by construction, which is what closes the header-splitting half
// of the defect. The percent-encoded form admits only the unreserved set, so Arabic, spaces, quotes and line
// breaks alike become escapes and cannot affect the header's structure.
//
// A name with nothing left after sanitisation, an all-Arabic name for instance, falls back to a generic word
// rather than an empty one. Some clients treat an empty name as no name and save the URL's last segment, which
// here is a bare identifier.

namespace MotsSupplierPortal.Infrastructure.Storage;

using System.Text;

public static class ContentDisposition
{
    private const string AsciiFallback = "download";

    public static string Attachment(string fileName)
    {
        var ascii = ToAsciiFallback(fileName);
        var encoded = PercentEncode(fileName);

        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{encoded}";
    }

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
