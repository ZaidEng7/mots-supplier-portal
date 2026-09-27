// Which refusals from the ERP are worth trying again, and which are worth telling somebody about.
//
// THIS IS THE ERP'S OWN TABLE rather than the usual "retry anything that is not a 4xx", and two entries are the
// reason it needs pinning. A 409 is retryable although it is a 4xx, because it means somebody edited the record
// between the read and the write. A 417 covers a broken business rule AND a query naming a field that does not
// exist, and neither improves by being sent again.
//
// THE THIRD OUTCOME EXISTS FOR A REASON. A revoked credential and a network wobble both end the request, but only
// one of them needs a person, and it needs them now: a nightly import that silently does nothing looks exactly
// like an import with no changes to make.
//
// THE CONTROL IS THE UNKNOWN STATUS. Treating what this does not recognise as permanent would turn a new gateway
// or proxy in front of the ERP into a permanently dead import with no retry, so the default is deliberately the
// forgiving one and is asserted rather than assumed.

namespace MotsSupplierPortal.Tests.Unit.Erp;

using System.Net;
using FluentAssertions;
using MotsSupplierPortal.Infrastructure.Integration.Erp;

public sealed class ErpFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErpFailureKind.CredentialOrPermission)]
    [InlineData(HttpStatusCode.Forbidden, ErpFailureKind.CredentialOrPermission)]
    [InlineData(HttpStatusCode.NotFound, ErpFailureKind.Permanent)]
    [InlineData(HttpStatusCode.ExpectationFailed, ErpFailureKind.Permanent)]
    [InlineData(HttpStatusCode.Conflict, ErpFailureKind.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, ErpFailureKind.Transient)]
    [InlineData(HttpStatusCode.RequestTimeout, ErpFailureKind.Transient)]
    [InlineData(HttpStatusCode.BadGateway, ErpFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErpFailureKind.Transient)]
    public void Each_refusal_is_classified_the_way_the_erps_own_table_says(
        HttpStatusCode status, ErpFailureKind expected)
    {
        ErpFailure.Classify(status).Should().Be(expected, $"{(int)status} {status}");
    }

    [Fact]
    public void A_conflict_is_retryable_although_it_is_a_client_error()
    {
        ErpFailure.ShouldRetry(HttpStatusCode.Conflict).Should().BeTrue(
            "it means the record changed between the read and the write, which is what a retry is for");
    }

    [Fact]
    public void A_broken_business_rule_is_not_retried()
    {
        ErpFailure.ShouldRetry(HttpStatusCode.ExpectationFailed).Should().BeFalse(
            "retrying identical data fails identically");
    }

    [Fact]
    public void A_status_this_does_not_know_is_treated_as_transient()
    {
        ErpFailure.Classify((HttpStatusCode)599).Should().Be(
            ErpFailureKind.Transient,
            "a new gateway in front of the ERP should not become a permanently dead import with no retry");
    }
}
