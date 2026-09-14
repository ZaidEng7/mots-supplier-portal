// A type that tracks expiry must be given a valid FUTURE date at upload, and a type that does not track expiry
// must never end up carrying one.
//
// Before this, a tracked type silently accepted nothing and accepted past dates. Both failures are quiet: the
// expiry job selects on the date being present, so a required-expiry document uploaded without one is never
// looked at again. It counts toward completeness forever and can never expire.
//
// The second half is made structural rather than trusted: the date is DISCARDED for a type that does not track
// expiry, so no such row can ever be picked up, rather than relying on callers not to send one. That it is
// discarded and therefore cannot be wrong is asserted, so nobody helpfully adds validation later and starts
// rejecting uploads for a field the type does not use.
//
// The boundary case: a document expiring today is not valid FOR today, and accepting it would make the first
// expiry run of the day transition a document that was just filed as current, which reads as a system fault
// rather than a rule.
//
//
// THE OUT-OF-RANGE DATES REPRODUCE A REAL SERVER ERROR
//
// The refusal message interpolates the date, and interpolation uses the current culture, which on an
// Arabic-locale host is a calendar supporting a limited range of years. Formatting outside it threw from inside
// the exception's own construction, so the guard that should have produced a clean refusal produced an
// unhandled error instead.
//
// The dates are chosen to sit outside that window, so this fails on ANY host if the formatting reverts to the
// current culture rather than only on an Arabic-locale one.
//
// Both are PAST dates. Only the rejection path formats the date, so a far-future out-of-range date is simply
// valid and never reaches the formatter; including one would assert a rejection that should not happen.

namespace MotsSupplierPortal.Tests.Unit.Domain;

using System.Globalization;
using FluentAssertions;
using MotsSupplierPortal.Domain.Suppliers;

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
        var act = () => Create(Today, expiryTracked: true);

        act.Should().Throw<DomainException>().WithMessage("*not in the future*");
    }

    [Fact]
    public void A_non_tracked_type_discards_an_expiry_date_rather_than_carrying_it()
    {
        var document = Create(Today.AddDays(30), expiryTracked: false);

        document.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void A_non_tracked_type_accepts_a_past_date_without_complaint()
    {
        var act = () => Create(Today.AddDays(-100), expiryTracked: false);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(1899, 12, 31)]
    [InlineData(1, 1, 1)]
    public void A_past_or_out_of_range_expiry_is_rejected_without_crashing_on_any_host_culture(
        int year, int month, int day)
    {
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
