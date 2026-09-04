using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-051 over HTTP: the clarification loop §4.1 defines and nothing could reach.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProposalClarificationLoopTests(PostgresApiFixture fixture)
{
    private static async Task GrantAsync(PostgresApiFixture fixture, string role, string permission)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();
        var appRole = await roleManager.FindByNameAsync(role);
        var claims = await roleManager.GetClaimsAsync(appRole!);
        if (claims.Any(c => c.Type == "perms" && c.Value == permission)) return;
        await roleManager.AddClaimAsync(appRole!, new System.Security.Claims.Claim("perms", permission));
    }

    /// <summary>Drives an RFQ to UnderEvaluation, which is what moves proposals into UnderReview.</summary>
    private async Task<(HttpClient Officer, HttpClient Supplier, string ProposalCode)> UnderReviewProposalAsync(string tag)
    {
        var seed = await EvaluationSeed.CreateAsync(fixture, tag);
        return (seed.Officer, seed.Supplier, seed.ProposalCode);
    }

    [Fact]
    public async Task Evaluation_intake_moves_submitted_proposals_to_UnderReview()
    {
        // The gateway. Asserted in STORAGE, because this is a state change nothing renders yet.
        var (_, _, proposalCode) = await UnderReviewProposalAsync($"Intake{Guid.NewGuid():N}"[..12]);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await db.Proposals.AsNoTracking()
            .Where(p => p.ReferenceCode == proposalCode).Select(p => p.State).FirstAsync();

        state.Should().Be(ProposalState.UnderReview,
            "opening evaluation is §4.1's 'Submitted -> UnderReview' trigger");
    }

    [Fact]
    public async Task The_clarification_loop_runs_over_HTTP_and_is_audited()
    {
        await GrantAsync(fixture, Roles.SupplierAdmin, Permissions.ProposalRevise);
        var (officer, supplier, proposalCode) = await UnderReviewProposalAsync($"Loop{Guid.NewGuid():N}"[..12]);

        var request = await officer.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "Confirm the delivery window." });
        request.StatusCode.Should().Be(HttpStatusCode.OK, await request.Content.ReadAsStringAsync());
        (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString()
            .Should().Be(nameof(ProposalState.ClarificationRequested));

        var revise = await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/revise", null);
        revise.StatusCode.Should().Be(HttpStatusCode.OK, await revise.Content.ReadAsStringAsync());
        (await revise.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString()
            .Should().Be(nameof(ProposalState.Revised));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Asserted against the stored rows, not the responses: §4.1 names both audit events.
        var actions = await db.AuditLogs.AsNoTracking()
            .Where(a => a.AggregateType == "Proposal" && a.ReferenceCode == proposalCode)
            .Select(a => a.Action).ToListAsync();

        actions.Should().Contain("proposal_clarification_requested").And.Contain("proposal_revised");

        var proposal = await db.Proposals.AsNoTracking().FirstAsync(p => p.ReferenceCode == proposalCode);
        proposal.RevisionNumber.Should().Be(2, "the original submission is revision 1");
        proposal.ClarificationReason.Should().Be("Confirm the delivery window.");
    }

    [Fact]
    public async Task A_clarification_without_a_reason_is_refused_and_with_one_is_not()
    {
        // The guard checked both ways: it can refuse, and it can be satisfied.
        var (officer, _, proposalCode) = await UnderReviewProposalAsync($"Reason{Guid.NewGuid():N}"[..12]);

        var without = await officer.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "" });
        without.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var with = await officer.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "A real question." });
        with.StatusCode.Should().Be(HttpStatusCode.OK, "control: the same call with a reason succeeds");
    }

    [Fact]
    public async Task Another_organizations_officer_cannot_request_clarification()
    {
        // §9.2, and the scope predicate is IN the query rather than checked afterwards.
        var (officer, _, proposalCode) = await UnderReviewProposalAsync($"Scope{Guid.NewGuid():N}"[..12]);

        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, otherOrg.Id);

        var refused = await outsider.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "Not mine." });
        refused.StatusCode.Should().Be(HttpStatusCode.NotFound, "§9.2: 404, never 403");

        // Owner control: the RFQ's own officer succeeds on the same URL, so the 404 is the scope
        // working rather than a route that refuses everyone.
        (await officer.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "Mine." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_supplier_cannot_revise_a_proposal_that_was_never_asked_to()
    {
        await GrantAsync(fixture, Roles.SupplierAdmin, Permissions.ProposalRevise);
        var (officer, supplier, proposalCode) = await UnderReviewProposalAsync($"NoAsk{Guid.NewGuid():N}"[..12]);

        var early = await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/revise", null);
        // 400, not §3's 409. Every proposal endpoint maps InvalidState to BadRequest - a convention
        // that predates this change and that §3 contradicts ("Illegal transitions return 409
        // Conflict ... listing the current state and the allowed next states"). Asserted as the code
        // actually behaves, and the divergence is recorded as T-065 rather than changed here, where
        // it would move every proposal endpoint at once.
        early.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "§4.1 only allows Revised from ClarificationRequested");

        // Control: once a clarification IS requested, the same call succeeds.
        await officer.PostAsJsonAsync(
            $"/api/v1/proposals/{proposalCode}/request-clarification", new { reason = "Please clarify." });
        (await supplier.PostAsync($"/api/v1/proposals/{proposalCode}/revise", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
