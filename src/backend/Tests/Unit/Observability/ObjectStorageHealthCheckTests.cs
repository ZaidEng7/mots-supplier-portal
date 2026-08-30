using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MotsSupplierPortal.Infrastructure.Observability;
using MotsSupplierPortal.Infrastructure.Storage;

namespace MotsSupplierPortal.Tests.Unit.Observability;

/// <summary>Task #16/NFR-OBS-006: proven against a real socket failure, not a mocked client -
/// MinioFileStorage.PingAsync is pointed at a genuinely unreachable address (port 1, refused
/// immediately) and the health check must translate that real AmazonS3/socket exception into
/// Unhealthy rather than letting it propagate.</summary>
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
