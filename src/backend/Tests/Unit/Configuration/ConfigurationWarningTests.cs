// The two startup warnings, and the cases where each must stay silent.
//
//
// THE EXPIRY WINDOW AND THE REMINDER LADDER
//
// They are independent numbers that coincide only at their shared default. Widen the window past the widest rung
// and a document sits in the expiring state with nobody told.
//
// That is accurately documented on both settings, and documentation is not where the person changing a
// configuration value is looking.
//
// A NARROWER window is not a misconfiguration: the rung simply falls due while the document is still approved,
// and it is still sent. Warning about it would train people to ignore this channel, which is how the one warning
// that matters gets lost.
//
// And with a wider rung configured, a wider window is entirely covered. Comparing against the shipped default
// instead of the configured ladder would fire a warning that is simply untrue, and a warning that is wrong is
// worse than none, because the next true one is not believed.
//
//
// RECURRING JOBS DISABLED OUTSIDE DEVELOPMENT
//
// A mistyped key cannot be caught by any test, because a test asserting the correct key passes whether or not
// the deployed environment reads the same one. So this covers the VALUE being set, which is what an operator can
// actually see and act on.
//
// Development turns them off deliberately, and the integration suite does exactly that, so warning there would
// fire on every local run and train the reader to ignore the whole channel.
//
//
// THE LOAD-BEARING CASE IN BOTH HALVES
//
// The shipped configuration must produce no warning at all. A warning that fires on the defaults is noise, and
// noise at boot is how a real warning gets ignored later.

namespace MotsSupplierPortal.Tests.Unit.Configuration;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MotsSupplierPortal.Api.Configuration;

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
        RequiredConfiguration.Warnings(WithWindowAndCadence(window, 30, 14, 3)).Should().BeEmpty();
    }

    [Fact]
    public void The_comparison_uses_the_configured_cadence_rather_than_the_default_one()
    {
        RequiredConfiguration.Warnings(WithWindowAndCadence(45, 60, 30, 14, 3)).Should().BeEmpty();
    }

    [Fact]
    public void Recurring_jobs_at_their_default_are_silent()
    {
        RequiredConfiguration.Warnings(Build(("ASPNETCORE_ENVIRONMENT", "Production")))
            .Should().NotContain(w => w.Contains("Jobs:EnableRecurring"));
    }

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

    [Fact]
    public void Recurring_jobs_disabled_in_development_is_silent()
    {
        RequiredConfiguration.Warnings(Build(
            ("ASPNETCORE_ENVIRONMENT", "Development"),
            ("Jobs:EnableRecurring", "false")))
            .Should().NotContain(w => w.Contains("Jobs:EnableRecurring"));
    }
}
