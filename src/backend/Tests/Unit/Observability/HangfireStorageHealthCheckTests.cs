// The job-storage readiness check's own failure handling, proven against a storage that genuinely throws.
//
// Not a stub configured to return a canned failure, but an actual implementation whose monitoring call fails,
// which is what a real unreachable storage backend would do.

namespace MotsSupplierPortal.Tests.Unit.Observability;

using FluentAssertions;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Observability;

public sealed class HangfireStorageHealthCheckTests
{
    private sealed class ThrowingJobStorage : Hangfire.JobStorage
    {
        public override IMonitoringApi GetMonitoringApi() => throw new InvalidOperationException("storage unreachable");
        public override IStorageConnection GetConnection() => throw new InvalidOperationException("storage unreachable");
    }

    [Fact]
    public async Task Reports_unhealthy_when_the_storage_genuinely_throws()
    {
        var check = new HangfireStorageHealthCheck(new ThrowingJobStorage());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
