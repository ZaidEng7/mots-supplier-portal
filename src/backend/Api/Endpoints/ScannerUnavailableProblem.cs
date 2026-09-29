// The answer a download gives when the virus scanner could not check the file this time.
//
// A 503 rather than the 404 an infected file gets, because nothing is wrong with the file as far as anyone knows and
// the reader should simply try again - a 404 would tell a supplier the tender specification is gone. It is only ever
// reached after the caller has been scoped to the file, so it tells an outsider nothing an in-scope reader does not
// already know. One wording for every download route, so a reader sees the same thing wherever it happens.

namespace MotsSupplierPortal.Api.Endpoints;

internal static class ScannerUnavailableProblem
{
    public static IResult Result() =>
        Results.Problem(
            title: "The file could not be checked for viruses right now.",
            detail: "The virus scanner is not available. The file is safe where it is; try again in a few minutes.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
}
