using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Domain.Proposals;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-028: proposal supporting files could be uploaded and deleted but never read, by anyone.
/// These tests cover the two halves separately because they answer to different rules - a supplier
/// reading their own bid is not gated at all, and a buyer reading someone else's is gated on the
/// evaluation reaching Consolidated (D-7).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ProposalDocumentDownloadTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_supplier_can_read_back_a_file_on_their_own_proposal_and_not_one_on_anothers()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Doc Own", withDocuments: true);

        var own = await seeded.Supplier.GetAsync(
            $"/api/v1/proposals/{seeded.ProposalCode}/documents/{seeded.CommercialDocumentId}/download-url");

        own.StatusCode.Should().Be(HttpStatusCode.OK,
            "a bidder reading their own attachment is not a two-envelope question");
        var body = await own.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("fileName").GetString().Should().Be("prices.pdf");
        body.GetProperty("url").GetString().Should().NotBeNullOrWhiteSpace();

        // The control on the guard: a DIFFERENT supplier holding the same document id gets nothing.
        // The id is the only thing a prober controls, so this is the case that matters.
        var other = await EvaluationSeed.CreateAsync(fixture, "Doc Other", withDocuments: true);
        var crossRead = await other.Supplier.GetAsync(
            $"/api/v1/proposals/{seeded.ProposalCode}/documents/{seeded.CommercialDocumentId}/download-url");
        crossRead.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "§9.2: out of scope is indistinguishable from does not exist");
    }

    [Fact]
    public async Task An_unstated_envelope_is_stored_as_Commercial_and_a_declared_one_is_kept()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Doc Envelope", withDocuments: true);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var documents = await db.ProposalDocuments.AsNoTracking()
            .Where(d => d.ProposalId == seeded.ProposalId).ToListAsync();

        documents.Single(d => d.Id == seeded.TechnicalDocumentId).Envelope
            .Should().Be(ProposalDocumentEnvelope.Technical, "the supplier declared it");
        documents.Single(d => d.Id == seeded.CommercialDocumentId).Envelope
            .Should().Be(ProposalDocumentEnvelope.Commercial,
                "D-7: a file nobody labelled is treated as pricing, because that failure is recoverable");
    }

    [Fact]
    public async Task A_buyer_sees_nothing_before_consolidation_and_both_envelopes_after_it()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Doc Gate", withDocuments: true);
        var listUrl = $"/api/v1/rfqs/{seeded.RfqCode}/evaluation/proposals/{seeded.ProposalId}/documents";
        var downloadUrl = $"{listUrl}/{seeded.TechnicalDocumentId}/download-url";

        // Before: the evaluation is open but not consolidated. Refused, and refused as a 404 rather
        // than an empty 200 - an attachment COUNT is itself a signal about a live competitor bid.
        (await seeded.Manager.GetAsync(listUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await seeded.Manager.GetAsync(downloadUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Drive the evaluation to Consolidated.
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });
        var criterionId = await CriterionIdAsync(seeded.EvaluationId);
        await seeded.Evaluator.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/scores", new
        {
            proposalId = seeded.ProposalId, criterionId, rawScore = 90m,
            commentAr = (string?)null, commentEn = (string?)null,
        });
        (await seeded.Evaluator.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/submit", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await seeded.Manager.PostAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/consolidate", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // After: the same two requests, the same caller, the opposite answer. That difference is the
        // gate - without this half the first two assertions would also pass on a route that always
        // refuses.
        var list = await seeded.Manager.GetAsync(listUrl);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await list.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        rows.Should().HaveCount(2);
        rows.Select(r => r.GetProperty("envelope").GetString())
            .Should().BeEquivalentTo(["Technical", "Commercial"],
                "consolidation opens both envelopes - the discriminator is stored for a later rule, not applied by this one");

        var download = await seeded.Manager.GetAsync(downloadUrl);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileName").GetString().Should().Be("spec.pdf");
    }

    [Fact]
    public async Task A_buyer_from_another_organization_is_refused_even_after_consolidation()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Doc Scope", withDocuments: true);
        var outsiderOrg = await OrganizationTestHelper.CreateOrganizationAsync(fixture);
        var outsider = await StaffTestClient.CreateAsync(fixture, Roles.ProcurementManager, outsiderOrg.Id);

        var response = await outsider.GetAsync(
            $"/api/v1/rfqs/{seeded.RfqCode}/evaluation/proposals/{seeded.ProposalId}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the row scope is applied in the query, so another organization's RFQ code is simply not found");
    }

    private async Task<Guid> CriterionIdAsync(Guid evaluationId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.EvaluationCriterionSnapshots
            .Where(c => c.EvaluationId == evaluationId).Select(c => c.Id).FirstAsync();
    }
}
