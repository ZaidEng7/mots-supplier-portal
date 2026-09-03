using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Suppliers;
using MotsSupplierPortal.Infrastructure.Persistence;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// RISK-004 (cross-tenant leakage - the risk register's only Critical), for the routes §12-A/C2
/// made code-addressed.
///
/// <para><b>Why these tests did not exist before and must exist now.</b> Every proposal route used
/// to live under <c>/api/v1/suppliers/me/rfqs/{rfqCode}/proposal/…</c>, a path with NO SLOT for
/// another supplier's identifier - the attack could not be expressed, so the negative could not be
/// written. §3 (<c>/proposals/{proposalCode}/items</c>) and §12.5
/// (<c>PATCH /proposals/{proposalCode}</c>) address a proposal by its own public code, which hands
/// every caller a way to name a proposal that is not theirs. That is a real property given up, and
/// this file is what replaces it.</para>
///
/// <para><b>404, not 403.</b> §9.2: *"Out-of-scope access to an existing resource returns 404 (not
/// 403) to avoid leaking existence."* Each case asserts the status AND that the response body is
/// byte-identical to the body for a proposal code that never existed - which is the property
/// "indistinguishable by design" actually names, and the same pattern
/// <see cref="CrossOrganizationScopeTests"/> already uses.</para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProposalCrossOrganizationScopeTests(PostgresApiFixture fixture)
{
    private const string NeverExistedProposalCode = "PRP-2026-999999";

    private async Task<(HttpClient Client, Guid SupplierId)> ActiveSupplierAsync(string name)
    {
        var (client, _) = await SupplierTestClient.CreateVerifiedSupplierWithEmailAsync(fixture, name);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var supplier = await db.Suppliers.FirstAsync(s => s.DisplayNameEn == name);
        await db.Suppliers.Where(s => s.Id == supplier.Id).ExecuteUpdateAsync(p => p
            .SetProperty(s => s.OnboardingState, SupplierOnboardingState.Approved)
            .SetProperty(s => s.LifecycleState, SupplierLifecycleState.Active));

        return (client, supplier.Id);
    }

    /// <summary>Publishes one RFQ inviting both suppliers, and returns A's own proposal code.</summary>
    private async Task<(HttpClient SupplierA, HttpClient SupplierB, string ProposalCodeOfA, Guid ItemId, Guid RequirementId)>
        RivalSuppliersOnOneRfqAsync()
    {
        var (supplierA, supplierAId) = await ActiveSupplierAsync($"PropScopeA {Guid.NewGuid():N}"[..26]);
        var (supplierB, supplierBId) = await ActiveSupplierAsync($"PropScopeB {Guid.NewGuid():N}"[..26]);

        var org = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var officer = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementOfficer, org.Id);
        var manager = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, org.Id);

        var templateResponse = await manager.PostAsJsonAsync("/api/v1/evaluation-templates",
            new { nameAr = "قالب", nameEn = $"Prop Scope {Guid.NewGuid():N}" });
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await manager.PostAsJsonAsync($"/api/v1/evaluation-templates/{templateId}/criteria", new
        {
            nameAr = "جودة", nameEn = "Quality", dimension = "Technical", weight = 100, maxScore = 100,
            threshold = 50, scoringType = "Numeric", guidanceAr = (string?)null, guidanceEn = (string?)null,
        });
        await manager.PostAsync($"/api/v1/evaluation-templates/{templateId}/activate", null);

        var created = await officer.PostAsJsonAsync("/api/v1/rfqs", new
        {
            titleAr = "طلب", titleEn = "Proposal Scope RFQ", descriptionAr = (string?)null, descriptionEn = (string?)null,
            currencyCode = "SYP", publishAt = (DateTimeOffset?)null,
            submissionOpensAt = DateTimeOffset.UtcNow.AddDays(1), submissionClosesAt = DateTimeOffset.UtcNow.AddDays(8),
            clarificationDeadlineAt = (DateTimeOffset?)null, evaluationTargetDate = (DateTimeOffset?)null,
        });
        created.EnsureSuccessStatusCode();
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceCode").GetString()!;

        var itemResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/items", new
        {
            titleAr = "بند", titleEn = "Item", specificationAr = (string?)null, specificationEn = (string?)null,
            categoryCode = "catering", quantity = 5, unitOfMeasureCode = "unit", isUnitPrice = true, isOptional = false,
        });
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var reqResponse = await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/requirements", new
        {
            textAr = "شرط", textEn = "Requirement", isMandatory = true, documentTypeCode = (string?)null,
        });
        reqResponse.EnsureSuccessStatusCode();
        var requirementId = (await reqResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requirements").EnumerateArray().Single().GetProperty("id").GetGuid();

        await officer.PutAsJsonAsync($"/api/v1/rfqs/{code}/evaluation-template", new { evaluationTemplateId = templateId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations", new { supplierId = supplierAId });
        await officer.PostAsJsonAsync($"/api/v1/rfqs/{code}/invitations", new { supplierId = supplierBId });
        await officer.PostAsync($"/api/v1/rfqs/{code}/submit-review", null);
        await manager.PostAsync($"/api/v1/rfqs/{code}/approve", null);
        (await officer.PostAsync($"/api/v1/rfqs/{code}/publish", null)).EnsureSuccessStatusCode();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MotsSupplierPortal.Infrastructure.Rfqs.RfqTimelineJob>()
                .RunAsync(CancellationToken.None);
        }

        var proposalCodeOfA = await supplierA.StartProposalAsync(code);
        return (supplierA, supplierB, proposalCodeOfA, itemId, requirementId);
    }

    /// <summary>
    /// The two responses must be indistinguishable in everything that could tell a caller whether
    /// the resource exists.
    ///
    /// <para><b>Byte equality no longer applies, and that is §7 working rather than a weakened
    /// assertion.</b> Every problem+json now carries <c>traceId</c> and <c>correlationId</c>, which
    /// differ per request by design, and <c>instance</c>, which echoes the path the CALLER sent.
    /// Two requests can never produce identical bytes again. None of those three can leak
    /// existence: the ids are random per request, and the path is the caller's own input - they
    /// already know which code they asked for.</para>
    ///
    /// <para>So the comparison is over the fields that DO discriminate - status, type, code, detail
    /// - and it is stricter in the way that matters: previously a difference in any of them would
    /// have been caught only incidentally by byte equality, and now each is named.</para>
    /// </summary>
    private static async Task AssertIndistinguishableAsync(HttpResponseMessage outOfScope, HttpResponseMessage neverExisted)
    {
        outOfScope.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out-of-scope access to an existing resource returns 404, not 403");
        neverExisted.StatusCode.Should().Be(outOfScope.StatusCode);

        var a = await outOfScope.Content.ReadFromJsonAsync<JsonElement>();
        var b = await neverExisted.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var discriminating in new[] { "type", "title", "status", "code", "detail" })
        {
            var inA = a.TryGetProperty(discriminating, out var va) ? va.ToString() : null;
            var inB = b.TryGetProperty(discriminating, out var vb) ? vb.ToString() : null;
            inA.Should().Be(inB,
                $"'{discriminating}' differing between an out-of-scope code and one that never " +
                "existed is exactly the existence oracle §9.2 forbids");
        }
    }

    private static async Task AssertIndistinguishableFromUnknownAsync(
        Func<string, Task<HttpResponseMessage>> attempt, string realCodeOfSomeoneElse)
    {
        await AssertIndistinguishableAsync(
            await attempt(realCodeOfSomeoneElse), await attempt(NeverExistedProposalCode));
    }

    [Fact]
    public async Task A_supplier_cannot_read_another_suppliers_proposal_by_its_code()
    {
        var (_, supplierB, proposalCodeOfA, _, _) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => supplierB.GetAsync($"/api/v1/proposals/{code}"), proposalCodeOfA);
    }

    [Fact]
    public async Task A_supplier_cannot_price_an_item_on_another_suppliers_proposal()
    {
        var (_, supplierB, proposalCodeOfA, itemId, _) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => ProposalPatch.PriceItemAsync(supplierB, code, itemId, 1m, 1m, (decimal?)null, 1, (string?)null, (string?)null ),
            proposalCodeOfA);
    }

    [Fact]
    public async Task A_supplier_cannot_set_terms_on_another_suppliers_proposal()
    {
        var (_, supplierB, proposalCodeOfA, _, _) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => ProposalPatch.SetTermsAsync(supplierB, code, new
            {
                currencyCode = "SYP", paymentTerms = "Net 30", incotermCode = "FOB",
                deliveryTermsAr = "٣ أيام", deliveryTermsEn = "3 days", warranty = (string?)null,
                validityStart = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date),
                validityEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.Date.AddDays(30)),
            }),
            proposalCodeOfA);
    }

    [Fact]
    public async Task A_supplier_cannot_answer_a_requirement_on_another_suppliers_proposal()
    {
        var (_, supplierB, proposalCodeOfA, _, requirementId) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => ProposalPatch.AnswerAsync(supplierB, code, requirementId, "نعم", "Yes" ),
            proposalCodeOfA);
    }

    /// <summary>
    /// The highest-stakes one: submitting someone else's proposal would move THEIR aggregate through
    /// its state machine, not merely read it.
    /// </summary>
    [Fact]
    public async Task A_supplier_cannot_submit_another_suppliers_proposal()
    {
        var (_, supplierB, proposalCodeOfA, _, _) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => supplierB.PostAsync($"/api/v1/proposals/{code}/submit", null), proposalCodeOfA);
    }

    [Fact]
    public async Task A_supplier_cannot_withdraw_another_suppliers_proposal()
    {
        var (_, supplierB, proposalCodeOfA, _, _) = await RivalSuppliersOnOneRfqAsync();

        await AssertIndistinguishableFromUnknownAsync(
            code => supplierB.PostAsJsonAsync($"/api/v1/proposals/{code}/withdraw", new { reason = "Not mine" }),
            proposalCodeOfA);
    }

    /// <summary>
    /// The control. Every negative above would also pass if the routes were simply broken for
    /// everyone, so A must succeed on A's own proposal by the same code B was refused.
    /// </summary>
    [Fact]
    public async Task The_owning_supplier_can_still_reach_their_own_proposal_by_code()
    {
        var (supplierA, _, proposalCodeOfA, itemId, _) = await RivalSuppliersOnOneRfqAsync();

        (await supplierA.GetAsync($"/api/v1/proposals/{proposalCodeOfA}")).StatusCode
            .Should().Be(HttpStatusCode.OK, "control: the owner reaches it by exactly the code B was refused");

        (await ProposalPatch.PriceItemAsync(supplierA, proposalCodeOfA, itemId, 5m, 10m, (decimal?)null, 3, (string?)null, (string?)null ))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
