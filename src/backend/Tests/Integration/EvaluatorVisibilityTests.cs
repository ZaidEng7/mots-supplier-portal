using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MotsSupplierPortal.Domain.Identity;
using MotsSupplierPortal.Infrastructure.Persistence;
using Xunit;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// T-067, established rather than assumed. The backlog says an evaluator receives proposal GUIDs and
/// no bid content. This suite reproduces the WHOLE surface an evaluator can reach, end to end, so
/// the claim is measured before anything is built on it.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class EvaluatorVisibilityTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task An_assigned_evaluator_receives_the_specification_and_the_technical_envelope_of_every_bid()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "Visibility", withDocuments: true);
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        var my = await seeded.Evaluator.GetFromJsonAsync<JsonElement>(
            $"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation");

        // The specification. Before T-067 an evaluator held neither rfq.read nor comparison.view, so
        // the requirement they were scoring against was not reachable from anywhere in the product.
        my.GetProperty("rfqTitleEn").GetString().Should().NotBeNullOrWhiteSpace();
        my.GetProperty("rfqItems").GetArrayLength().Should().Be(1, "the seeded RFQ has one item");

        // The bid.
        var bid = my.GetProperty("proposals").EnumerateArray().Single();
        bid.GetProperty("proposalCode").GetString().Should().Be(seeded.ProposalCode);
        bid.GetProperty("supplierDisplayNameEn").GetString().Should().NotBeNullOrWhiteSpace(
            "D-19: the bidder's identity is shown, because BRULE-067's recusal is unusable without it");
        bid.GetProperty("technicallyQualified").ValueKind.Should().Be(JsonValueKind.False);

        // Documents: the TECHNICAL one only. The seed uploads one Technical and one Commercial, so
        // this is a guard checked both ways in a single assertion rather than a filter that could be
        // passing because nothing was there.
        var documents = bid.GetProperty("documents").EnumerateArray().ToList();
        documents.Should().ContainSingle("only the Technical document is listed");
        documents[0].GetProperty("originalFileName").GetString().Should().Be("spec.pdf");
        my.ToString().Should().NotContain("prices.pdf",
            "D-7: the Commercial-envelope document is not named to an evaluator, not even as a filename");
    }

    [Fact]
    public async Task An_evaluators_workspace_carries_no_commercial_field_of_any_name()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "NoPricing", withDocuments: true);
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        var raw = (await seeded.Evaluator.GetFromJsonAsync<JsonElement>(
            $"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation")).ToString();

        // The seeded proposal IS priced - EvaluationSeed prices its item before submitting - so this
        // is the negative with its control built in: the pricing exists and does not appear.
        foreach (var commercial in new[]
                 { "unitPrice", "lineTotal", "grandTotal", "currency", "paymentTerms", "incotermCode", "validityStart" })
        {
            raw.Should().NotContain(commercial,
                $"'{commercial}' is the commercial envelope; an evaluator sees it after consolidation, in the comparison matrix, and nowhere else");
        }

        // T-068: and no raw GUID identifies a proposal anywhere on this response.
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proposalId = await db.Proposals.Where(p => p.ReferenceCode == seeded.ProposalCode)
            .Select(p => p.Id).FirstAsync();
        raw.Should().NotContain(proposalId.ToString(),
            "§3: internal identifiers never appear in payloads");
    }

    [Fact]
    public async Task A_technical_document_opens_for_an_assigned_evaluator_and_a_commercial_one_does_not()
    {
        var seeded = await EvaluationSeed.CreateAsync(fixture, "EvalDoc", withDocuments: true);
        await seeded.Manager.PostAsJsonAsync($"/api/v1/rfqs/{seeded.RfqCode}/evaluation/assignments",
            new { evaluatorUserIds = new[] { seeded.EvaluatorId } });

        var basePath = $"/api/v1/rfqs/{seeded.RfqCode}/my-evaluation/proposals/{seeded.ProposalCode}/documents";

        // Satisfiable.
        var technical = await seeded.Evaluator.GetAsync($"{basePath}/{seeded.TechnicalDocumentId}/download-url");
        technical.StatusCode.Should().Be(HttpStatusCode.OK, await technical.Content.ReadAsStringAsync());
        (await technical.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileName").GetString().Should().Be("spec.pdf");

        // Refusable, on the envelope - the SAME caller, the same route, a real document id.
        var commercial = await seeded.Evaluator.GetAsync($"{basePath}/{seeded.CommercialDocumentId}/download-url");
        commercial.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a Commercial document is the same 404 as one that does not exist - an evaluator who " +
            "learns a bid has commercial attachments has learned something before consolidation");

        // Refusable, on the assignment - a different evaluator, the same technical document.
        var (outsider, _) = await StaffTestClient.CreateWithIdAsync(fixture, Roles.Evaluator, seeded.OrgId);
        (await outsider.GetAsync($"{basePath}/{seeded.TechnicalDocumentId}/download-url"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the assignment is the scope");
    }

    [Fact]
    public async Task An_evaluator_holds_neither_rfq_read_nor_comparison_view()
    {
        // The permission-set half of the same finding, asserted against the seeded role rather than
        // the constant, so a change to either shows up here.
        Roles.DefaultPermissions[Roles.Evaluator].Should().BeEquivalentTo(new[]
        {
            Permissions.EvaluationScore, Permissions.EvaluationSubmit, Permissions.RfqClarify,
        });
    }
}
