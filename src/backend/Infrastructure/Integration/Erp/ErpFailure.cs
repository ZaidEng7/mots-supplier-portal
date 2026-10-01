// What to do about a refusal from the ERP.
//
// THE ERP'S OWN DOCUMENTATION CARRIES THIS TABLE and it is not the usual "retry anything that is not a 4xx".
// Two of its statuses are worth reading twice: a 417 covers both a broken business rule and a query naming a
// field that does not exist, and a 403 means either the header never arrived or the credential has no rights on
// that record type. Neither is worth retrying, and both are worth telling somebody about.
//
// THE THREE OUTCOMES ARE DELIBERATELY NOT "retry or fail". A credential that has been revoked and a network
// wobble both end the request, but only one of them should reach a person - and reach them now rather than in a
// weekly log review, because the hourly import silently doing nothing looks identical to an import with no
// changes to make.
//
// NOTHING ACTS ON THE CLASSIFICATION YET. ErpRequestException carries its Kind, but only tests read it or ShouldRetry:
// ErpSupplierSyncJob retries every failure twice whatever the ERP answered, and the connection test sorts 401 and 403
// by rules of its own. Today a revoked credential reaches a person as a failed run recorded on the integrations
// screen, like any other failure.
//
// 409 IS RETRYABLE EXACTLY ONCE and that is the ERP's instruction, not a guess: it means somebody edited the
// record between the read and the write. This import only reads today, so it should not see one; when the
// writing side arrives, retrying a stale write in a loop is how two systems livelock.
//
// A STATUS THIS DOES NOT KNOW IS TREATED AS TRANSIENT. The alternative - treating the unknown as permanent -
// turns a new gateway or proxy in front of the ERP into a permanently broken import with no retry.
//
// THE PUSH TO THE ERP CLASSIFIES AGAIN, ON TOP OF THIS TABLE, because a write can fail in a way a read cannot: the
// request may have done its work although no answer came back. ClassifyPush sorts every call the push makes, reads
// included, into five kinds, and the method matters:
//
//   Permission       401 or 403. The credential is wrong or lacks a right, which no retry fixes and which fails every
//                    supplier alike, so the caller pauses the whole push rather than failing suppliers one by one.
//   AlreadyExists    a create answered 409, or DuplicateEntryError or UniqueValidationError under any status. The ERP
//                    has the record, and the caller looks it up instead of creating it. Frappe answers a clash on a
//                    unique field with 417 rather than 409, which is why the exc_type is read too.
//   Permanent        404 or 417 otherwise: a record type or record that is not there, a mandatory field, a link to a
//                    value the ERP does not hold. Sending the same thing again fails the same way, so it carries the
//                    ERP's own message for a person to act on.
//   OutcomeUnknown   a create that got no answer, a timeout (408), or a 5xx - a 502 included, which is also what no
//                    answer at all is reported as. The ERP may have made the record before the answer was lost, and it
//                    makes a second one for a second request, so the caller looks before it ever creates again.
//   Transient        429 and everything else, including a 5xx or no answer on a read or a PUT, which change nothing
//                    or replace what they send and so are safe to repeat.
//
// SupplierErpPushJob ACTS ON THE PUSH'S CLASSIFICATION. It stops the whole run on Permission and fails the push on
// Permanent. It looks in the ERP before anything else when the Supplier's own create is OutcomeUnknown or AlreadyExists,
// and counts every other failure as an attempt to try again later.

namespace MotsSupplierPortal.Infrastructure.Integration.Erp;

using System.Net;

public enum ErpFailureKind
{
    Transient,
    Permanent,
    CredentialOrPermission,
}

public enum ErpPushFailureKind
{
    Transient,
    Permission,
    Permanent,
    AlreadyExists,
    OutcomeUnknown,
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

    public static ErpPushFailureKind ClassifyPush(HttpMethod method, HttpStatusCode status, string? excType)
    {
        var create = method == HttpMethod.Post;

        if (Classify(status) == ErpFailureKind.CredentialOrPermission) return ErpPushFailureKind.Permission;

        if (create && (status == HttpStatusCode.Conflict || excType is "DuplicateEntryError" or "UniqueValidationError"))
        {
            return ErpPushFailureKind.AlreadyExists;
        }

        if (Classify(status) == ErpFailureKind.Permanent) return ErpPushFailureKind.Permanent;

        if (create && (status == HttpStatusCode.RequestTimeout || (int)status >= 500))
        {
            return ErpPushFailureKind.OutcomeUnknown;
        }

        return ErpPushFailureKind.Transient;
    }
}

// The ERP's refusal, or its silence, as the ERP clients report it. ErpMessage is the ERP's own sentence when it sent
// one, and Method is the request's, which a read leaves as GET; PushKind is ClassifyPush over the two.
public sealed class ErpRequestException(
    HttpStatusCode status,
    string? excType,
    string message,
    string? erpMessage = null,
    HttpMethod? method = null) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string? ExcType { get; } = excType;

    public ErpFailureKind Kind { get; } = ErpFailure.Classify(status);

    public string? ErpMessage { get; } = erpMessage;

    public HttpMethod Method { get; } = method ?? HttpMethod.Get;

    public ErpPushFailureKind PushKind => ErpFailure.ClassifyPush(Method, Status, ExcType);
}
