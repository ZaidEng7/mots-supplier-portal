// How the push sorts a failed call to the ERP, on top of the import's table in ErpFailure.
//
// THE METHOD IS HALF OF THE ANSWER. A 502 on a read is worth trying again, and the same 502 on a create may mean the
// supplier was made and the answer lost - where trying again makes a second supplier. So the rows that differ by
// method are asserted in pairs, and a classifier that ignored the method fails one of each pair.
//
// A CLASH ON A UNIQUE FIELD IS "ALREADY EXISTS" ALTHOUGH THE ERP ANSWERS 417 FOR IT, and only on a create. A 417 on a
// change is the ERP refusing the data, which is permanent, so that row is here too.
//
// THE CONTROL IS "ANYTHING ELSE", which must be transient: a status this does not know should cost a retry, not a
// supplier stuck as failed.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Net;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpPushFailureTests
{
    public static TheoryData<string, HttpStatusCode, string?, ErpPushFailureKind> Table() => new()
    {
        { "POST", HttpStatusCode.Unauthorized, "AuthenticationError", ErpPushFailureKind.Permission },
        { "POST", HttpStatusCode.Forbidden, "PermissionError", ErpPushFailureKind.Permission },
        { "PUT", HttpStatusCode.Forbidden, "PermissionError", ErpPushFailureKind.Permission },
        { "GET", HttpStatusCode.Forbidden, "PermissionError", ErpPushFailureKind.Permission },
        { "POST", HttpStatusCode.NotFound, "DoesNotExistError", ErpPushFailureKind.Permanent },
        { "PUT", HttpStatusCode.NotFound, "DoesNotExistError", ErpPushFailureKind.Permanent },
        { "POST", HttpStatusCode.ExpectationFailed, "MandatoryError", ErpPushFailureKind.Permanent },
        { "POST", HttpStatusCode.ExpectationFailed, "LinkValidationError", ErpPushFailureKind.Permanent },
        { "POST", HttpStatusCode.Conflict, null, ErpPushFailureKind.AlreadyExists },
        { "PUT", HttpStatusCode.Conflict, null, ErpPushFailureKind.Transient },
        { "POST", HttpStatusCode.ExpectationFailed, "UniqueValidationError", ErpPushFailureKind.AlreadyExists },
        { "POST", HttpStatusCode.ExpectationFailed, "DuplicateEntryError", ErpPushFailureKind.AlreadyExists },
        { "PUT", HttpStatusCode.ExpectationFailed, "UniqueValidationError", ErpPushFailureKind.Permanent },
        { "POST", HttpStatusCode.RequestTimeout, null, ErpPushFailureKind.OutcomeUnknown },
        { "POST", HttpStatusCode.InternalServerError, null, ErpPushFailureKind.OutcomeUnknown },
        { "POST", HttpStatusCode.BadGateway, null, ErpPushFailureKind.OutcomeUnknown },
        { "POST", HttpStatusCode.ServiceUnavailable, null, ErpPushFailureKind.OutcomeUnknown },
        { "POST", HttpStatusCode.GatewayTimeout, null, ErpPushFailureKind.OutcomeUnknown },
        { "GET", HttpStatusCode.BadGateway, null, ErpPushFailureKind.Transient },
        { "PUT", HttpStatusCode.BadGateway, null, ErpPushFailureKind.Transient },
        { "GET", HttpStatusCode.RequestTimeout, null, ErpPushFailureKind.Transient },
        { "POST", HttpStatusCode.TooManyRequests, null, ErpPushFailureKind.Transient },
        { "GET", HttpStatusCode.TooManyRequests, null, ErpPushFailureKind.Transient },
        { "POST", HttpStatusCode.BadRequest, null, ErpPushFailureKind.Transient },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_failure_is_sorted_by_its_method_status_and_exc_type(
        string method, HttpStatusCode status, string? excType, ErpPushFailureKind expected)
    {
        ErpFailure.ClassifyPush(new HttpMethod(method), status, excType)
            .Should().Be(expected, $"{method} {(int)status} {excType}");
    }

    [Fact]
    public void A_refusal_carries_its_method_into_its_push_kind()
    {
        new ErpRequestException(HttpStatusCode.BadGateway, null, "no answer", method: HttpMethod.Post)
            .PushKind.Should().Be(ErpPushFailureKind.OutcomeUnknown);
    }

    [Fact]
    public void A_refusal_without_a_method_is_a_read()
    {
        new ErpRequestException(HttpStatusCode.BadGateway, null, "no answer")
            .PushKind.Should().Be(ErpPushFailureKind.Transient,
                "the import's reads build their refusals without a method, and a read is safe to repeat");
    }
}
