// The caller's own supplier code, which the supplier routes are addressed by.
//
// Before that move the path said "me" and no lookup was required.
//
// It is read from the real self-read route, which is what a real client would use for exactly this, rather than
// reaching into the database. So the helper exercises the same path the interface does instead of quietly
// depending on test-only access.

namespace MotsSupplierPortal.Tests.Integration;

using System.Net.Http.Json;
using System.Text.Json;

internal static class SupplierCodeTestClient
{
    public static async Task<string> OwnSupplierCodeAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/suppliers/me");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("supplierCode").GetString()!;
    }
}
