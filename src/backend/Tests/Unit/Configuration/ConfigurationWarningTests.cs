using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Api.Configuration;

namespace MotsSupplierPortal.Tests.Unit.Configuration;

/// <summary>
/// The ExpiringSoon window and the BRULE-025 reminder ladder are independent numbers that coincide
/// only at their shared default of 30. Widen the window past the widest rung and the document sits
/// in ExpiringSoon with nobody told - accurately documented on both settings, and documentation is
/// not where the person changing a config value is looking.
/// </summary>
public sealed class ConfigurationWarningTests
{
    private static IConfiguration Build(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static IConfiguration WithWindowAndCadence(int window, params int[] cadence) =>
        Build([
            ("Documents:ExpiringSoonWindowDays", window.ToString()),
            .. cadence.Select((d, i) => ($"Documents:RenewalReminderDays:{i}", d.ToString())),
        ]);

    [Fact]
    public void The_defaults_are_silent()
    {
        // The load-bearing case. A warning that fires on the shipped configuration is noise, and
        // noise at boot is how a real warning gets ignored later.
        RequiredConfiguration.Warnings(Build()).Should().BeEmpty();
    }

    [Fact]
    public void A_window_wider_than_the_widest_rung_is_reported_with_the_size_of_the_silence()
    {
        var warnings = RequiredConfiguration.Warnings(WithWindowAndCadence(45, 30, 14, 3));

        warnings.Should().ContainSingle()
            .Which.Should().Contain("15 days",
                "the useful part is how long the supplier hears nothing, not that two numbers differ");
    }

    [Fact]
    public void The_warning_names_the_remedy_rather_than_only_the_problem()
    {
        var warnings = RequiredConfiguration.Warnings(WithWindowAndCadence(45, 30, 14, 3));

        warnings.Single().Should().Contain("RenewalReminderDays",
            "a boot warning that does not say what to change is a warning that gets muted");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(14)]
    public void A_window_at_or_below_the_widest_rung_is_silent(int window)
    {
        // A NARROWER window is not a misconfiguration: the rung simply falls due while the document
        // is still Approved, and it is still sent. Warning about it would train people to ignore
        // this channel, which is how the one warning that matters gets lost.
        RequiredConfiguration.Warnings(WithWindowAndCadence(window, 30, 14, 3)).Should().BeEmpty();
    }

    [Fact]
    public void The_comparison_uses_the_configured_cadence_rather_than_the_default_one()
    {
        // With a 60-day rung configured, a 45-day window is entirely covered. Comparing against the
        // hard-coded 30 would fire a warning that is simply untrue - and a warning that is wrong is
        // worse than none, because the next true one is not believed.
        RequiredConfiguration.Warnings(WithWindowAndCadence(45, 60, 30, 14, 3)).Should().BeEmpty();
    }

    // ---- MSP-98: recurring jobs disabled outside Development ---------------------------------

    /// <summary>
    /// The same load-bearing case as the defaults test above: the shipped configuration sets nothing,
    /// Jobs:EnableRecurring defaults to true, and a warning that fires on it would be noise.
    /// </summary>
    [Fact]
    public void Recurring_jobs_at_their_default_are_silent()
    {
        RequiredConfiguration.Warnings(Build(("ASPNETCORE_ENVIRONMENT", "Production")))
            .Should().NotContain(w => w.Contains("Jobs:EnableRecurring"));
    }

    /// <summary>
    /// The case the warning exists for. A typo'd key cannot be caught by any test - a test asserting
    /// the correct key passes whether or not the deployed environment reads the same one - so this
    /// covers the value being set, which is what an operator can actually see and act on.
    /// </summary>
    [Fact]
    public void Recurring_jobs_disabled_in_production_warns_about_the_consequence()
    {
        var warnings = RequiredConfiguration.Warnings(Build(
            ("ASPNETCORE_ENVIRONMENT", "Production"),
            ("Jobs:EnableRecurring", "false")));

        var warning = warnings.Should().ContainSingle(w => w.Contains("Jobs:EnableRecurring")).Subject;
        warning.Should().Contain("submission windows",
            "the message has to say what stops working, not that a scheduler is off - the person " +
            "reading it at boot knows what a tender is and may not know what a recurring job is");
    }

    /// <summary>
    /// Development turns them off deliberately - the integration suite does exactly this - so
    /// warning there would fire on every local run and train the reader to ignore the whole channel.
    /// </summary>
    [Fact]
    public void Recurring_jobs_disabled_in_development_is_silent()
    {
        RequiredConfiguration.Warnings(Build(
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("Jobs:EnableRecurring", "false")))
            .Should().NotContain(w => w.Contains("Jobs:EnableRecurring"));
    }
}
