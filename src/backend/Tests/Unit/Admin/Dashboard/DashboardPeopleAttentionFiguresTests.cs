// The figures the needs attention section reads from the people and access section.
//
// They are computed from the section's own record rather than counted again, and this checks each one is wired to
// the figure it names. Every input figure is a different number, so a name wired to the wrong figure, a staff
// figure read from the supplier side, or the used or still-valid invitations read as lapsed, gives a wrong answer
// rather than a lucky one.
//
// It also checks they stay off the wire. Every number in them is already in the answer, and a second copy beside
// the first would be a second thing a screen could read and drift from.

namespace MotsSupplierPortal.Tests.Unit.Admin.Dashboard;

using System.Text.Json;
using FluentAssertions;
using MotsSupplierPortal.Application.Admin.Dashboard;
using Xunit;

public sealed class DashboardPeopleAttentionFiguresTests
{
    private static readonly DashboardPeopleAndAccessDto Section = new(
        Staff: new DashboardAccountsDto(
            Active: 101, Inactive: 102, ActiveWithARole: 103, ActiveSessions: 104, PeopleWithActiveSessions: 105,
            PendingInvitations: 106, LockedOut: 107, CannotSignIn: 108),
        Suppliers: new DashboardAccountsDto(
            Active: 201, Inactive: 202, ActiveWithARole: 203, ActiveSessions: 204, PeopleWithActiveSessions: 205,
            PendingInvitations: 206, LockedOut: 207, CannotSignIn: 208),
        ActiveUsersByRole: [],
        StaffInvitedNeverSignedIn: new DashboardStaffInvitedNeverSignedInDto(
            LinkStillValid: 301, LinkExpired: 302, NoLinkYet: 303, LinkUsed: 304),
        TwoFactorRequiredRoles: [],
        SupplierLoginsOnPlaceholderAddresses: 401,
        OrganisationsByType: []);

    [Fact]
    public void Each_figure_is_the_one_it_names()
    {
        Section.NeedsAttention.Should().Be(new DashboardPeopleAttentionFigures(
            LockedOutStaff: 107,
            LockedOutSuppliers: 207,
            LapsedStaffInvitations: 302,
            StaffWhoCannotSignIn: 108,
            SuppliersWhoCannotSignIn: 208));
    }

    [Fact]
    public void They_are_not_sent_to_the_browser()
    {
        var json = JsonSerializer.Serialize(Section, JsonSerializerOptions.Web);

        JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().NotContain("needsAttention");
    }
}
