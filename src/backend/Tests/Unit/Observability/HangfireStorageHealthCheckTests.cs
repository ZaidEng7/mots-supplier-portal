using FluentAssertions;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MotsSupplierPortal.Infrastructure.Observability;

namespace MotsSupplierPortal.Tests.Unit.Observability;

/// <summary>Task #16/NFR-OBS-006: the health check's own try/catch behavior, proven against a real
/// storage that genuinely throws - not a mock configured to return a canned failure, an actual
/// implementation whose GetMonitoringApi() call fails, matching what a real unreachable Hangfire
/// storage backend would do.</summary>
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
