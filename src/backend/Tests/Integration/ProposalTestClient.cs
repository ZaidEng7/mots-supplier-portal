using System.Net.Http.Json;
using System.Text.Json;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §12-A/C2: proposals are addressed by their own public code now
/// (§3 <c>/proposals/{proposalCode}/items</c>, §12.5 <c>POST /proposals/{proposalCode}/submit</c>),
/// so a test that acts on a proposal has to know that code. Before the move every route was keyed
/// on the RFQ code the test already had.
///
/// <para>One helper rather than the same three lines in nine files - and it returns the code from
/// the CREATE response rather than querying for it, which is how a real client learns it (§12.5
/// documents <c>Location: /api/v1/proposals/PRO-2026-000891</c> on create).</para>
/// </summary>
internal static class ProposalTestClient
{
    /// <summary>Starts a proposal on the given RFQ and returns its public reference code.</summary>
    public static async Task<string> StartProposalAsync(this HttpClient client, string rfqReferenceCode)
    {
        var response = await client.PostAsync($"/api/v1/rfqs/{rfqReferenceCode}/proposals", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("referenceCode").GetString()!;
    }
}
