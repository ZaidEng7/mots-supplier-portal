using System.Net.Http.Json;
using System.Text.Json;

namespace MotsSupplierPortal.Tests.Integration;

/// <summary>
/// §12-A/C3: the supplier routes are addressed by <c>{supplierCode}</c> now (§12.2, §12.3), so a
/// test acting on its own supplier needs that code. Before the move the path said <c>me</c> and no
/// lookup was required.
///
/// <para>Read from <c>GET /api/v1/suppliers/me</c> - a real route a real client would use for
/// exactly this - rather than reaching into the database, so the helper exercises the same path the
/// SPA does instead of quietly depending on test-only access.</para>
/// </summary>
internal static class SupplierCodeTestClient
{
    public static async Task<string> OwnSupplierCodeAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/suppliers/me");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("referenceCode").GetString()!;
    }
}
