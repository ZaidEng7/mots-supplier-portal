// What to do about a refusal from the ERP.
//
// THE ERP'S OWN DOCUMENTATION CARRIES THIS TABLE and it is not the usual "retry anything that is not a 4xx".
// Two of its statuses are worth reading twice: a 417 covers both a broken business rule and a query naming a
// field that does not exist, and a 403 means either the header never arrived or the credential has no rights on
// that record type. Neither is worth retrying, and both are worth telling somebody about.
//
// THE THREE OUTCOMES ARE DELIBERATELY NOT "retry or fail". A credential that has been revoked and a network
// wobble both end the request, but only one of them should reach a person - and reach them now rather than in a
// weekly log review, because the nightly import silently doing nothing looks identical to an import with no
// changes to make.
//
// 409 IS RETRYABLE EXACTLY ONCE and that is the ERP's instruction, not a guess: it means somebody edited the
// record between the read and the write. This import only reads today, so it should not see one; when the
// writing side arrives, retrying a stale write in a loop is how two systems livelock.
//
// A STATUS THIS DOES NOT KNOW IS TREATED AS TRANSIENT. The alternative - treating the unknown as permanent -
// turns a new gateway or proxy in front of the ERP into a permanently broken import with no retry.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net;

public enum ErpFailureKind
{
    Transient,
    Permanent,
    CredentialOrPermission,
}

public static class ErpFailure
{
    public static ErpFailureKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => ErpFailureKind.CredentialOrPermission,
        HttpStatusCode.Forbidden => ErpFailureKind.CredentialOrPermission,
        HttpStatusCode.NotFound => ErpFailureKind.Permanent,
        HttpStatusCode.Conflict => ErpFailureKind.Transient,
        HttpStatusCode.ExpectationFailed => ErpFailureKind.Permanent,
        HttpStatusCode.TooManyRequests => ErpFailureKind.Transient,
        HttpStatusCode.RequestTimeout => ErpFailureKind.Transient,
        _ => ErpFailureKind.Transient,
    };

    public static bool ShouldRetry(HttpStatusCode status) => Classify(status) == ErpFailureKind.Transient;
}

public sealed class ErpRequestException(
    HttpStatusCode status,
    string? excType,
    string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string? ExcType { get; } = excType;

    public ErpFailureKind Kind { get; } = ErpFailure.Classify(status);
}
