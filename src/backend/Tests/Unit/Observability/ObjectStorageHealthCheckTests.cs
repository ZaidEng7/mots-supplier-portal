// The object-storage readiness check, proven against a real socket failure rather than a stubbed client.
//
// The probe is pointed at a genuinely unreachable address, where the connection is refused immediately, and the
// check must translate that real exception into an unhealthy result rather than letting it propagate.

namespace MotsSupplierPortal.Tests.Unit.Observability;

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Observability;
using MotsSupplierPortal.Infrastructure.Storage;

public sealed class ObjectStorageHealthCheckTests
{
    [Fact]
    public async Task Reports_unhealthy_when_the_endpoint_is_genuinely_unreachable()
    {
        var storage = new MinioFileStorage(Options.Create(new MinioOptions
        {
            Endpoint = "127.0.0.1:1",
            AccessKey = "unused",
            SecretKey = "unused",
            UseSsl = false,
            Bucket = "unused",
        }));
        var check = new ObjectStorageHealthCheck(storage);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
