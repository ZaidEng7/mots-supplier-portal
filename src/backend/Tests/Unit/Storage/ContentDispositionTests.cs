// The filename in a download header is attacker-supplied: it is whatever the uploader typed.
//
// Interpolated raw, a double quote closes the quoted string early and everything after it is parsed as further
// header parameters. The assertions pin exactly the three parameters this emits, with no fourth smuggled in by
// the name, and check the quotes are gone from the fallback rather than escaped into it.
//
//
// THE LINE-BREAK HALF IS ASSERTED ON THE PROPERTY, NOT ON COSMETICS
//
// A raw carriage return or line feed ends the header line, and whatever follows becomes a header of its own.
//
// The security property is that no line ends: with neither character in the value there is no second header,
// whatever the text says. Words that look like a header name surviving INSIDE a quoted parameter value are
// inert, and asserting they are absent would be asserting cosmetics. The first version of this test did exactly
// that and failed for the wrong reason.
//
//
// THE CONTROL THAT MATTERS MOST
//
// An ASCII-only escape would pass both assertions above and destroy every Arabic filename in this product,
// which is a regression dressed as a fix.
//
// The ASCII fallback is still a usable word rather than empty, because an empty filename makes some clients save
// the URL's last segment, which here is a bare identifier.
//
// And the other control: the common case must not be mangled into escapes in the part a human reads.

namespace MotsSupplierPortal.Tests.Unit.Storage;

using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class ContentDispositionTests
{
    [Fact]
    public void A_quote_in_the_file_name_cannot_break_out_of_the_header()
    {
        var header = ContentDisposition.Attachment("in\"jected\"; x=y.pdf");

        header.Split(';').Should().HaveCount(3);
        header.Should().StartWith("attachment; filename=\"");
        header.Should().Contain("filename*=UTF-8''");

        var fallback = header.Split(';')[1];
        fallback.Count(c => c == '"').Should().Be(2, "only the two that delimit the value");
    }

    [Fact]
    public void CRLF_in_the_file_name_cannot_start_a_new_header()
    {
        var header = ContentDisposition.Attachment("evil\r\nSet-Cookie: a=b.pdf");

        header.Should().NotContain("\r").And.NotContain("\n");
        header.Split(';').Should().HaveCount(3, "no extra parameter was smuggled in");
    }

    [Fact]
    public void An_Arabic_file_name_survives_the_round_trip()
    {
        const string arabic = "السجل التجاري.pdf";

        var header = ContentDisposition.Attachment(arabic);

        var encoded = header.Split("filename*=UTF-8''")[1];
        Uri.UnescapeDataString(encoded).Should().Be(arabic, "the real name is recoverable byte for byte");

        header.Should().Contain("filename=\".pdf\"");
    }

    [Fact]
    public void An_ordinary_file_name_is_left_readable()
    {
        var header = ContentDisposition.Attachment("commercial-register.pdf");

        header.Should().Be(
            "attachment; filename=\"commercial-register.pdf\"; filename*=UTF-8''commercial-register.pdf");
    }

    [Fact]
    public void A_name_with_nothing_ASCII_left_still_gets_a_fallback()
    {
        var header = ContentDisposition.Attachment("مستند");

        header.Should().Contain("filename=\"download\"", "never an empty filename");
        Uri.UnescapeDataString(header.Split("filename*=UTF-8''")[1]).Should().Be("مستند");
    }
}
