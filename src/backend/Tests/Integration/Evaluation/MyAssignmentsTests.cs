// SCR-500, FR-DSH-004 and T3-02 - the screen that makes EPIC-11 reachable by the persona it was built for.
//
// FR-DSH-008 and RISK-004. A dashboard is the widest cross-aggregate read in the product, and cross-tenant
// leakage is the risk register's only Critical. Every assertion here has an owner control beside it, so a
// negative cannot pass because the query is broken - and the counts are asserted as well as the rows,
// because "3 assignments" that includes someone else's is a leak even when no row is shown.
//
// The assign helper goes through the real endpoint rather than the change tracker. Appending an assignment
// to a loaded aggregate and saving reproduces the misdetection this codebase has hit before - EF classifies
// the new child as Modified and issues an UPDATE against a row that does not exist - and driving the API
// instead tests the path production uses.
//
// The read helper FAILS WITH THE BODY when the response is not a 200. GetFromJsonAsync throws on a
// non-success status and deserialises a problem+json object into a JsonElement that then throws again on
// GetArrayLength, so either way the failure names neither the status nor the reason. This suite produced
// exactly that once in a full run - 638 tests, one failure, green in isolation - and the anonymity is why it
// could not be diagnosed: T-087's lesson, applied where it recurred.
//
// An evaluator sees their own assignments and never another's, with the control that the assignment really
// is visible to the person who holds it, and the negative asserted at count level in the same breath: B sees
// no rows AND no volume. An evaluator with no organization still sees their assignments, which is the reason
// this handler is scoped by assignment rather than by organization, asserted rather than trusted -
// LoadScopedByAssignmentAsync's own note says an evaluator need not belong to the procuring organization,
// and an evaluator with OrganizationId null is exactly who a normally-scoped widget would show nothing.
//
// A recused evaluator stops being asked, with the control first: it is on the dashboard before the recusal.
//
// The tabs are IA §4.3's three names - Assigned, In Progress, Submitted - and a fresh assignment is
// Assigned. Both directions of the filter gate are asserted, because an unknown tab dropped rather than
// refused returns everything to a caller who asked to narrow.
//
// Progress counts what this evaluator owes rather than what the committee does, and a persona without
// evaluation.score cannot read the screen.

namespace MotsSupplierPortal.Tests.Integration.Evaluation;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using MotsSupplierPortal.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class MyAssignmentsTests(PostgresApiFixture fixture)
{
    private static async Task AssignAsync(Seeded seeded, Guid evaluatorUserId)
    {
        var response = await seeded.Manager.PostAsJsonAsync(
            $"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { evaluatorUserId } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> AssignmentsAsync(HttpClient client, string? tab = null)
    {
        var response = await client.GetAsync($"/api/v1/my-evaluations{(tab is null ? "" : $"?tab={tab}")}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array, body);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task An_evaluator_sees_their_own_assignments_and_never_another_evaluators()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignA");
        var (clientA, evaluatorA) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);
        var (clientB, evaluatorB) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);

        await AssignAsync(seeded, evaluatorA);

        var mine = await AssignmentsAsync(clientA);
        var theirs = await AssignmentsAsync(clientB);

        mine.EnumerateArray().Select(a => a.GetProperty("rfqReferenceCode").GetString())
            .Should().Contain(seeded.RfqCode, "control: the assigned evaluator sees their own work");

        theirs.GetArrayLength().Should().Be(0,
            "an evaluator must see neither the rows nor the number of someone else's assignments");
        _ = evaluatorB;
    }

    [Fact]
    public async Task An_evaluator_with_no_organization_still_sees_their_assignments()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignNoOrg");
        var (client, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, organizationId: null);

        await AssignAsync(seeded, evaluatorId);

        var assignments = await AssignmentsAsync(client);

        assignments.GetArrayLength().Should().Be(1,
            "assignment scoping must not depend on an organization the evaluator may not have");
    }

    [Fact]
    public async Task A_recused_evaluator_stops_being_asked()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignRecused");
        var (client, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);
        await AssignAsync(seeded, evaluatorId);

        var before = await AssignmentsAsync(client);
        before.GetArrayLength().Should().Be(1);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evaluation = await db.Evaluations.Include(e => e.Assignments).FirstAsync(e => e.Id == seeded.EvaluationId);
            evaluation.RecuseEvaluator(evaluatorId, "Conflict of interest");
            await db.SaveChangesAsync();
        }

        var after = await AssignmentsAsync(client);

        after.GetArrayLength().Should().Be(0, "a recused evaluator is no longer being asked for anything");
    }

    [Fact]
    public async Task The_tabs_are_the_three_IA_names_and_an_unknown_one_is_refused()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignTabs");
        var (client, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);
        await AssignAsync(seeded, evaluatorId);

        var assigned = await AssignmentsAsync(client, "Assigned");
        assigned.GetArrayLength().Should().Be(1);

        var submitted = await AssignmentsAsync(client, "Submitted");
        submitted.GetArrayLength().Should().Be(0, "nothing has been submitted yet");

        var unknown = await client.GetAsync("/api/v1/my-evaluations?tab=Everything");
        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Progress_counts_what_this_evaluator_owes_not_what_the_committee_does()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignProgress");
        var (client, evaluatorId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);
        await AssignAsync(seeded, evaluatorId);

        var assignments = await AssignmentsAsync(client);
        var row = assignments.EnumerateArray().Single();

        row.GetProperty("scoresRecorded").GetInt32().Should().Be(0);
        row.GetProperty("scoresExpected").GetInt32().Should().Be(seeded.CriterionCount * seeded.SubmittedProposalCount,
            "the denominator is this evaluator's own workload: one score per criterion per submitted proposal");
    }

    [Fact]
    public async Task A_persona_without_evaluation_score_cannot_read_the_screen()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "MyAssignDenied");
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, seeded.OrgId);

        var response = await officer.GetAsync("/api/v1/my-evaluations");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "IA §4.3 gates My Evaluations on evaluation.score");
    }
}
