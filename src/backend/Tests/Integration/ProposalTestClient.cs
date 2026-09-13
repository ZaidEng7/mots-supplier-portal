// Starting a bid, and editing one through the route that now carries those edits.
//
// Bids are addressed by their own public code, so a test that acts on one has to know that code. Before that
// move every route was keyed on the tender code the test already had.
//
// One helper rather than the same three lines in nine files, and it reads the code from the CREATE response
// rather than querying for it, which is how a real client learns it: the contract documents it in the response's
// location header.
//
//
// THE EDIT HELPERS MODEL THE REAL CLIENT
//
// The five per-field edit routes are gone, so these send the equivalent partial update and the suite keeps
// asserting the same behaviour through the route that now carries it.
//
// Pricing MERGES rather than replaces, because the standard replaces an array wholesale and the tests that price
// two lines do it in two calls.
//
// Reading the bid first and sending the full array is exactly what the editor does, so the helper models the real
// client rather than papering over the semantics.

namespace MotsSupplierPortal.Tests.Integration;

using System.Net.Http.Json;
using System.Text.Json;

internal static class ProposalTestClient
{
    public static async Task<string> StartProposalAsync(this HttpClient client, string rfqReferenceCode)
    {
        var response = await client.PostAsync($"/api/v1/rfqs/{rfqReferenceCode}/proposals", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("proposalCode").GetString()!;
    }
}

public static class ProposalPatch
{
    private const string MergePatch = "application/merge-patch+json";

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, string proposalCode, object patch)
    {
        using var content = new StringContent(JsonSerializer.Serialize(patch), System.Text.Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MergePatch);

        return await client.PatchAsync($"/api/v1/proposals/{proposalCode}", content);
    }

    public static Task<HttpResponseMessage> SetTermsAsync(HttpClient client, string proposalCode, object terms) =>
        SendAsync(client, proposalCode, new { commercialTerms = terms });

    public static Task<HttpResponseMessage> SetNarrativeAsync(HttpClient client, string proposalCode, string? ar, string? en) =>
        SendAsync(client, proposalCode, new { technicalResponse = new { narrativeAr = ar, narrativeEn = en } });

    public static Task<HttpResponseMessage> AnswerAsync(HttpClient client, string proposalCode, Guid requirementId, string ar, string en) =>
        SendAsync(client, proposalCode, new
        {
            technicalResponse = new { answers = new[] { new { requirementId, answerAr = ar, answerEn = en } } },
        });

    public static async Task<HttpResponseMessage> PriceItemAsync(
        HttpClient client, string proposalCode, Guid rfqItemId,
        decimal quantity, decimal unitPrice, decimal? discount = null, int? leadTimeDays = null,
        string? notesAr = null, string? notesEn = null)
    {
        var items = await CurrentItemsAsync(client, proposalCode);
        items.RemoveAll(i => i.rfqItemId == rfqItemId);
        items.Add(new PricedItem(rfqItemId, quantity, unitPrice, discount, leadTimeDays, notesAr, notesEn));

        return await SendAsync(client, proposalCode, new { items });
    }

    public static async Task<HttpResponseMessage> RemoveItemAsync(HttpClient client, string proposalCode, Guid rfqItemId)
    {
        var items = await CurrentItemsAsync(client, proposalCode);
        items.RemoveAll(i => i.rfqItemId == rfqItemId);

        return await SendAsync(client, proposalCode, new { items });
    }

    private static async Task<List<PricedItem>> CurrentItemsAsync(HttpClient client, string proposalCode)
    {
        var read = await client.GetAsync($"/api/v1/proposals/{proposalCode}");
        if (!read.IsSuccessStatusCode) return [];

        var proposal = await read.Content.ReadFromJsonAsync<JsonElement>();
        if (!proposal.TryGetProperty("items", out var items)) return [];

        return [.. items.EnumerateArray().Select(i => new PricedItem(
            i.GetProperty("rfqItemId").GetGuid(),
            i.GetProperty("quantity").GetDecimal(),
            i.GetProperty("unitPrice").GetDecimal(),
            i.GetProperty("discount").ValueKind == JsonValueKind.Null ? null : i.GetProperty("discount").GetDecimal(),
            i.GetProperty("leadTimeDays").ValueKind == JsonValueKind.Null ? null : i.GetProperty("leadTimeDays").GetInt32(),
            i.GetProperty("notesAr").ValueKind == JsonValueKind.Null ? null : i.GetProperty("notesAr").GetString(),
            i.GetProperty("notesEn").ValueKind == JsonValueKind.Null ? null : i.GetProperty("notesEn").GetString()))];
    }

    private sealed record PricedItem(
        Guid rfqItemId, decimal quantity, decimal unitPrice, decimal? discount,
        int? leadTimeDays, string? notesAr, string? notesEn);
}
