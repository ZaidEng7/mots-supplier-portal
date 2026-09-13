// The rate-limit rejection counter actually records a measurement on a real rejection.
//
// Captured through a real measurement listener from the base library, with no extra test package, rather than by
// asserting on internal state. So this proves what an actual metrics exporter would see.
//
//
// THE LISTENER'S LIFETIME IS THE CALLER'S, AND THAT IS NOT A STYLE POINT
//
// It must be disposed AFTER the calls expected to record measurements. Disposing it early, for instance through
// a helper that returns before the real work happens, silently stops listening with no error.
//
// That is exactly the defect this shape had on the first pass, and it was caught by the very revert-to-red proof
// it exists to support: the positive test failed with zero measurements before the fix.
//
//
// IT FILTERS BY METER INSTANCE, NOT BY METER NAME
//
// Every test in this class and in the rate-limiter tests constructs its own metrics object, and every one of
// those meters shares the same name, because the name is a constant.
//
// The test runner runs different test classes in parallel by default, so a listener filtering on name alone
// hears every instance in the process rather than the one this test constructed. This test failed in continuous
// integration on exactly that, picking up a concurrently running rate-limiter rejection as if it were its own.
//
// Comparing the meter object itself scopes the listener to only the instance this test owns, which is what
// "records a measurement" is actually supposed to mean here.

namespace MotsSupplierPortal.Tests.Unit.Observability;

using System.Diagnostics.Metrics;
using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Observability;

public sealed class AppMetricsTests
{
    private static (MeterListener Listener, List<long> Values, List<IReadOnlyDictionary<string, object?>> Tags) Listen(Meter meter, string instrumentName)
    {
        var values = new List<long>();
        var tags = new List<IReadOnlyDictionary<string, object?>>();

        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == instrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tagSpan, state) =>
        {
            values.Add(measurement);
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tagSpan) dict[tag.Key] = tag.Value;
            tags.Add(dict);
        });
        listener.Start();

        return (listener, values, tags);
    }

    [Fact]
    public void A_per_target_rejection_records_a_measurement_with_surface_and_layer_tags()
    {
        using var metrics = new AppMetrics();
        var (listener, values, tags) = Listen(metrics.Meter, "mots.rate_limit.rejections");
        using var _ = listener;

        using var limiter = new PerTargetRateLimiter(metrics);
        for (var i = 0; i < 5; i++)
        {
            limiter.TryAcquire("register", "metrics-probe@example.com");
        }
        limiter.TryAcquire("register", "metrics-probe@example.com");

        values.Should().ContainSingle().Which.Should().Be(1);
        tags.Should().ContainSingle();
        tags[0]["surface"].Should().Be("register");
        tags[0]["layer"].Should().Be("per-target");
    }

    [Fact]
    public void Requests_within_budget_never_record_a_rejection()
    {
        using var metrics = new AppMetrics();
        var (listener, values, _) = Listen(metrics.Meter, "mots.rate_limit.rejections");
        using var _2 = listener;

        using var limiter = new PerTargetRateLimiter(metrics);
        limiter.TryAcquire("login", "well-behaved@example.com");

        values.Should().BeEmpty("a request inside its budget is not a rejection - no measurement should exist to see");
    }
}
