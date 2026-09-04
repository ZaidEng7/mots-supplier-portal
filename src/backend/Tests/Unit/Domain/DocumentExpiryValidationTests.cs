using System.Globalization;
using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// BRULE-020 (MSP-68): a type that tracks expiry must be given a valid FUTURE expiry date at
/// upload, and a type that does not track expiry must never end up carrying one.
///
/// Before this, ExpiryTracked=true silently accepted null and past dates. Both failures are quiet:
/// DocumentExpiryJob filters on `ExpiryDate != null`, so a required-expiry document uploaded
/// without a date is simply never looked at again - it counts toward completeness forever and can
/// never expire.
/// </summary>
public sealed class DocumentExpiryValidationTests
{
    private static readonly DateOnly Today = new(2026, 8, 29);

    private static SupplierDocument Create(DateOnly? expiry, bool expiryTracked) =>
        SupplierDocument.CreatePendingScan(
            $"DOC-2026-{Guid.NewGuid().ToString("N")[..6]}",
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "quarantine/key",
            "cert.pdf", "application/pdf", 1024, Guid.CreateVersion7(),
            issueDate: null, expiryDate: expiry, expiryTracked: expiryTracked, today: Today);

    [Fact]
    public void A_tracked_type_accepts_a_future_expiry()
    {
        var document = Create(Today.AddDays(1), expiryTracked: true);

        document.ExpiryDate.Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void A_tracked_type_rejects_a_missing_expiry()
    {
        var act = () => Create(null, expiryTracked: true);

        act.Should().Throw<DomainException>().WithMessage("*requires an expiry date*",
            "without a date the expiry job never sees this document again - it counts toward " +
            "completeness forever and can never expire");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3650)]
    public void A_tracked_type_rejects_a_past_expiry(int daysFromToday)
    {
        var act = () => Create(Today.AddDays(daysFromToday), expiryTracked: true);

        act.Should().Throw<DomainException>().WithMessage("*not in the future*");
    }

    [Fact]
    public void Today_is_not_a_valid_expiry()
    {
        // The boundary. A document expiring today is not valid *for* today - and accepting it would
        // make the first expiry-job run of the day transition a document that was just filed as
        // current, which reads as a system fault rather than a rule.
        var act = () => Create(Today, expiryTracked: true);

        act.Should().Throw<DomainException>().WithMessage("*not in the future*");
    }

    [Fact]
    public void A_non_tracked_type_discards_an_expiry_date_rather_than_carrying_it()
    {
        // BRULE-020's second half, made structural: "types without expiry never enter
        // ExpiringSoon/Expired". The job selects on ExpiryDate != null, so a discarded date means
        // no such row can ever be picked up - rather than relying on callers not to send one.
        var document = Create(Today.AddDays(30), expiryTracked: false);

        document.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void A_non_tracked_type_accepts_a_past_date_without_complaint()
    {
        // It is discarded, so it cannot be wrong. Asserted so nobody "helpfully" adds validation
        // here later and starts rejecting uploads for a field the type does not use.
        var act = () => Create(Today.AddDays(-100), expiryTracked: false);

        act.Should().NotThrow();
    }

    [Theory]
    // Both are PAST dates outside Umm al-Qura's 1900-2077 window. Only the rejection path formats
    // the date, so a far-future out-of-range date (2100) is simply valid and never reaches the
    // formatter - including it here would assert a rejection that should not happen.
    [InlineData(1899, 12, 31)]
    [InlineData(1, 1, 1)]
    public void A_past_or_out_of_range_expiry_is_rejected_without_crashing_on_any_host_culture(
        int year, int month, int day)
    {
        // Reproduces a real 500. The rejection message interpolates the date, and interpolation
        // uses CurrentCulture - which on an Arabic-locale host is the Umm al-Qura calendar,
        // supporting only 1900-2077 Gregorian. Formatting outside that range threw
        // ArgumentOutOfRangeException from inside the exception's own construction, so the guard
        // that should have produced a clean 400 produced an unhandled 500.
        //
        // The dates here are chosen to sit outside that window, so this fails on ANY host if the
        // formatting reverts to CurrentCulture - not only on an Arabic-locale one.
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var act = () => Create(new DateOnly(year, month, day), expiryTracked: true);

            act.Should().Throw<DomainException>()
                .WithMessage("*not in the future*",
                    "the guard must reject the date, not crash while explaining why it rejected it");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
