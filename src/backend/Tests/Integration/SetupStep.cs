// A setup call that names itself when it fails.
//
//
// WHY THIS IS SHARED RATHER THAN PRIVATE TO ONE SEED
//
// A multi-step arrangement that discards each response reports its failure at the first step that happens to be
// asserted, which is usually several calls later and about something else.
//
// A full run failed three bid tests on a publish refusing a transition, three lines after an approve, an
// invitation and a review submission whose responses were thrown away. Nothing in that message says which of
// them did not happen, and the tests pass in isolation, so the evidence is gone by the time anyone looks.
//
// The evaluation seed already had exactly this helper, written for exactly this reason. A second copy beside it
// would be the drift this repository keeps finding in its instruments, so it moved here and that seed now calls
// it.

namespace MotsSupplierPortal.Tests.Integration;

internal static class SetupStep
{
    public static async Task<HttpResponseMessage> Of(string owner, string name, Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (response.IsSuccessStatusCode) return response;

        throw new InvalidOperationException(
            $"{owner} could not complete '{name}': {(int)response.StatusCode} "
            + $"{response.StatusCode}. Body: {await response.Content.ReadAsStringAsync()}");
    }
}
