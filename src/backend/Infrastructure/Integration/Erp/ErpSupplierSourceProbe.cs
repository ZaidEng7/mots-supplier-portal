// Asking the ERP whether it is reachable with the credentials currently in force.
//
// IT READS THE COMPANY, one record, which is the cheapest call the ERP's own documentation names for exactly this
// purpose. A test that listed suppliers would take as long as an import and would fail for reasons that are not
// about reachability.
//
// IT REPORTS RATHER THAN THROWS. "No" is a legitimate answer to "can you reach it", and the caller stores the
// answer either way. The detail carries the ERP's own words where there are any, because "403 PermissionError"
// tells an administrator to ask the other team and a generic failure sends them to check their typing.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net.Http.Headers;
using MotsSupplierPortal.Application.Integration;

public interface IErpSupplierSourceProbe
{
    Task<IntegrationTestResult> TryReachAsync(CancellationToken ct);
}

public sealed class ErpSupplierSourceProbe(
    HttpClient client,
    IErpConnectionProvider connections) : IErpSupplierSourceProbe
{
    public async Task<IntegrationTestResult> TryReachAsync(CancellationToken ct)
    {
        var connection = await connections.CurrentAsync(ct);

        if (connection is null)
        {
            return new IntegrationTestResult(false, "No address is configured.");
        }

        try
        {
            var url = ErpQuery.List("Company", ["name"], limitPageLength: 1);

            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(connection.BaseUrl.TrimEnd('/') + "/"), url));

            request.Headers.Authorization =
                new AuthenticationHeaderValue("token", $"{connection.ApiKey}:{connection.ApiSecret}");

            using var response = await client.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return new IntegrationTestResult(true, $"Reached {connection.BaseUrl} and read one record.");
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            return new IntegrationTestResult(
                false,
                $"{(int)response.StatusCode} {response.StatusCode}. {Excerpt(body)}".TrimEnd());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new IntegrationTestResult(false, exception.Message);
        }
    }

    // The ERP answers a refusal with a full traceback, which is useful to the team who own it and unreadable on a
    // screen. The first part carries the exception type, which is the part that names the fix.
    private static string Excerpt(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";
}
