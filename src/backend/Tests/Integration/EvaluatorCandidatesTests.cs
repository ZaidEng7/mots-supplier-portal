using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// <c>GET /rfqs/{code}/evaluation/candidates</c>, which exists because the assign control on SCR-430
/// was a free-text GUID box: the only staff list in the product needs <c>admin.users.manage</c>, which
/// a procurement_manager does not hold, so the step was unusable without database access.
///
/// <para><b>The list is derived from ROLE CLAIMS, not from Roles.DefaultPermissions.</b> The catalogue
/// is the shipped default; what a role actually grants is its claim rows, which an administrator can
/// edit on SCR-703. Reading the catalogue would list a candidate who no longer holds evaluation.score,
/// or omit one who was granted it, and the assignment would then fail at the point of SCORING - the
/// worst place to find out. The test below revokes the claim from the role and expects the candidate to
/// disappear, which is the only way to tell the two sources apart.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvaluatorCandidatesTests(PostgresApiFixture fixture)
{
    private static async Task<List<JsonElement>> CandidatesAsync(HttpClient client, string rfqCode)
    {
        var response = await client.GetAsync($"/api/v1/rfqs/{rfqCode}/evaluation/candidates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().ToList();
    }

    [Fact]
    public async Task Lists_the_evaluators_in_the_rfqs_own_organisation()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Candidates");

        var candidates = await CandidatesAsync(seeded.Manager, seeded.RfqCode);

        candidates.Should().Contain(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);
        // The name and email are the whole point - a picker of GUIDs is what this replaced.
        var evaluator = candidates.First(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);
        evaluator.GetProperty("fullName").GetString().Should().NotBeNullOrWhiteSpace();
        evaluator.GetProperty("email").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Excludes_an_evaluator_from_another_organisation()
    {
        // BRULE-029. Offering one would be a scoping hole in a picker rather than in a query, which is
        // harder to notice and just as effective.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "CandidatesScope");
        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var (_, outsiderId) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, otherOrg.Id);

        var candidates = await CandidatesAsync(seeded.Manager, seeded.RfqCode);

        candidates.Should().Contain(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);
        candidates.Should().NotContain(c => c.GetProperty("userId").GetGuid() == outsiderId);
    }

    [Fact]
    public async Task Excludes_a_deactivated_account()
    {
        // An inactive account cannot sign in, so assigning one parks the evaluation on somebody who can
        // never submit it - and nothing about the assignment itself would look wrong.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "CandidatesInactive");

        (await CandidatesAsync(seeded.Manager, seeded.RfqCode))
            .Should().Contain(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users.Where(u => u.Id == seeded.EvaluatorId)
                .ExecuteUpdateAsync(p => p.SetProperty(u => u.IsActive, false));
        }

        (await CandidatesAsync(seeded.Manager, seeded.RfqCode))
            .Should().NotContain(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);
    }

    [Fact]
    public async Task Follows_the_roles_claims_rather_than_the_shipped_catalogue()
    {
        // The assertion that distinguishes the two sources: Roles.DefaultPermissions is unchanged
        // throughout, and only the claim rows move.
        //
        // Scoped to a THROWAWAY role, and that is not tidiness. The first version of this test revoked
        // evaluation.score from the shared Evaluator role, which is global state in one database: it
        // passed alone and took its own sibling down in a full run, since every other test's evaluator
        // then had no scoring claim either. Same class of fault as the dev seeder polluting this
        // fixture - a suite that only fails when run together is the expensive kind.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "CandidatesClaims");
        var roleName = $"scorer-{Guid.NewGuid():N}"[..24];
        Guid standInId;

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var role = new IdentityRole<Guid> { Id = Guid.CreateVersion7(), Name = roleName };
            (await roleManager.CreateAsync(role)).Succeeded.Should().BeTrue();
            await roleManager.AddClaimAsync(role, new Claim("perms", Permissions.EvaluationScore));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser
            {
                Id = Guid.CreateVersion7(),
                UserName = $"standin-{Guid.NewGuid():N}@ministry.example",
                Email = $"standin-{Guid.NewGuid():N}@ministry.example",
                FullName = "Claim Stand-in",
                EmailConfirmed = true,
                IsActive = true,
                OrganizationId = seeded.OrgId,
            };
            user.UserName = user.Email;
            (await userManager.CreateAsync(user, "IntegrationPassw0rd!")).Succeeded.Should().BeTrue();
            await userManager.AddToRoleAsync(user, roleName);
            standInId = user.Id;
        }

        // Present on the strength of a claim the shipped catalogue says nothing about.
        (await CandidatesAsync(seeded.Manager, seeded.RfqCode))
            .Should().Contain(c => c.GetProperty("userId").GetGuid() == standInId);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var roleId = await db.Set<IdentityRole<Guid>>().AsNoTracking()
                .Where(r => r.Name == roleName).Select(r => r.Id).FirstAsync();
            await db.Set<IdentityRoleClaim<Guid>>()
                .Where(c => c.RoleId == roleId && c.ClaimType == "perms" && c.ClaimValue == Permissions.EvaluationScore)
                .ExecuteDeleteAsync();
        }

        (await CandidatesAsync(seeded.Manager, seeded.RfqCode))
            .Should().NotContain(c => c.GetProperty("userId").GetGuid() == standInId);
        // And the seeded evaluator is untouched, which is what proves the revoke was scoped.
        (await CandidatesAsync(seeded.Manager, seeded.RfqCode))
            .Should().Contain(c => c.GetProperty("userId").GetGuid() == seeded.EvaluatorId);
    }

    [Fact]
    public async Task Answers_404_for_an_rfq_in_another_organisation()
    {
        // §9.2: the scope predicate is in the query, so an RFQ outside the caller's organisation is
        // ABSENT rather than forbidden - a 403 would confirm the reference code names something real.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "CandidatesForeign");
        var otherOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, otherOrg.Id);

        var response = await outsider.GetAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/candidates");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refuses_a_caller_without_the_assign_permission()
    {
        // The control on the permission itself: the evaluator who APPEARS in this list cannot read it.
        var seeded = await EvaluationSeed.CreateAsync(fixture, "CandidatesPerm");

        var response = await seeded.Evaluator.GetAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/candidates");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
