using System.Diagnostics.Metrics;
using FluentAssertions;
using MotsSupplierPortal.Api.Authorization;
using MotsSupplierPortal.Infrastructure.Observability;

namespace MotsSupplierPortal.Tests.Unit.Observability;

/// <summary>Task #16/NFR-OBS-006: the rate-limit rejection counter actually records a measurement
/// on a real rejection - captured via a real MeterListener (BCL, no extra test package) rather
/// than asserting on internal state, so this proves what an actual OTel exporter would see.</summary>
public sealed class AppMetricsTests
{
    /// <summary>The caller owns the returned listener and must dispose it AFTER making the calls
    /// expected to record measurements - disposing it early (e.g. via a "using" local to a helper
    /// that returns before the real work happens) silently stops listening with no error, which is
    /// exactly the bug this shape had on the first pass (caught by the very revert-to-red proof it
    /// exists to support: the "positive" test failed with zero measurements before this fix).
    ///
    /// <para><b>Task #17: filters by Meter instance, not by Meter.Name.</b> Every test in this class
    /// and in PerTargetRateLimiterTests constructs its own <c>new AppMetrics()</c>, and every one of
    /// those Meters shares the same name ("MotsSupplierPortal" - AppMetrics.MeterName is a constant).
    /// xUnit runs different test classes in parallel by default; a MeterListener filtering on name
    /// alone hears every AppMetrics instance in the process, not just the one this test constructed -
    /// this test failed in CI on exactly that, picking up a concurrently-running
    /// PerTargetRateLimiterTests rejection as if it were its own. Comparing the Meter object itself
    /// scopes the listener to only the instance this test owns, which is what "records a measurement"
    /// is actually supposed to mean here.</para>
    /// </summary>
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
        // The 6th exceeds the 5/min "register" budget - this is the rejection under test.
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
