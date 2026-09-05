using FluentAssertions;
using MotsSupplierPortal.Domain.Configuration;
using Xunit;

namespace MotsSupplierPortal.Tests.Unit.Domain;

/// <summary>
/// T-060. The validation lives on the definition so there is exactly one copy of each rule; these
/// assert the rules themselves, which the integration tests then prove are actually reached.
/// </summary>
public sealed class SystemSettingDefinitionTests
{
    private static SettingDefinition Definition(string key) => SystemSettings.Find(key)!;

    [Theory]
    [InlineData("30", true)]
    [InlineData("1", true)]
    [InlineData("365", true)]
    [InlineData("0", false)]
    [InlineData("366", false)]
    [InlineData("-1", false)]
    [InlineData("thirty", false)]
    [InlineData("30.5", false)]
    [InlineData("", false)]
    public void The_expiry_window_accepts_a_day_count_inside_its_bounds(string value, bool expected)
    {
        (Definition(SystemSettings.ExpiringSoonWindowDays).Validate(value) is null).Should().Be(expected);
    }

    [Theory]
    [InlineData("30,14,3", null)]
    [InlineData("3", null)]
    [InlineData(" 30 , 14 ", null)]              // trimmed, because a pasted value carries spaces
    [InlineData("30,14,14", "value_has_duplicates")]
    [InlineData("30,400", "value_out_of_range")]
    [InlineData("30,x", "value_out_of_range")]
    [InlineData(",", "value_required")]
    public void The_reminder_ladder_refuses_a_repeated_rung(string value, string? expected)
    {
        // A repeated rung is not harmless: the reminder ledger keys on the threshold value, so the
        // second send is suppressed and the setting behaves differently from what it says.
        Definition(SystemSettings.RenewalReminderDays).Validate(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("open", null)]
    [InlineData("closed", null)]
    [InlineData("Open", "value_not_allowed")]     // ordinal, so a case slip is a refusal not a silent miss
    [InlineData("invite-only", "value_not_allowed")]
    public void Registration_mode_is_one_of_two_words(string value, string? expected)
    {
        Definition(SystemSettings.RegistrationMode).Validate(value).Should().Be(expected);
    }

    [Fact]
    public void Every_definitions_own_default_is_valid_under_its_own_rules()
    {
        // The defaults are what a fresh deployment runs on and what an unparseable value degrades to.
        // A default that fails its own validation would be a setting nobody could restore.
        foreach (var definition in SystemSettings.All)
        {
            definition.Validate(definition.DefaultValue)
                .Should().BeNull($"{definition.Key}'s default '{definition.DefaultValue}' must satisfy its own rules");
        }
    }

    [Fact]
    public void The_public_allow_list_names_only_settings_that_exist()
    {
        // A typo here would be a key that silently never appears in the public response.
        foreach (var key in SystemSettings.PubliclyReadable)
        {
            SystemSettings.Find(key).Should().NotBeNull(key);
        }

        // And the ones NOT on it stay off it: the expiry cadence is operational detail an
        // unauthenticated visitor has no reason to read.
        SystemSettings.PubliclyReadable.Should().NotContain(SystemSettings.ExpiringSoonWindowDays);
        SystemSettings.PubliclyReadable.Should().NotContain(SystemSettings.RenewalReminderDays);
    }
}
