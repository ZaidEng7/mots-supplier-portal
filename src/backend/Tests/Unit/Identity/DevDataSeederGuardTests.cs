using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Infrastructure.Identity;

namespace MotsSupplierPortal.Tests.Unit.Identity;

/// <summary>
/// The environment guard on <see cref="DevDataSeeder"/>.
///
/// <para>This seeder creates eight accounts whose password is a compile-time constant published in
/// RUNBOOK.md. For most of this batch the only thing standing between that and production was an
/// <c>IsDevelopment()</c> block around the call site in Program.cs — so a second call added anywhere, or
/// that block being widened, would have seeded known credentials into whatever environment was running.
/// The refusal now lives in the seeder, where the dangerous knowledge is.</para>
///
/// <para><b>Why this test and not a fuller one.</b> Everything past the guard needs a database and a
/// UserManager, and the integration fixture deliberately does not run this seeder — it runs as
/// Development, so before <c>DevSeed:Enabled</c> gated it, its five suppliers turned up in the middle of
/// ReviewQueuePaginationTests' assertions. The guard is the part whose failure has consequences beyond a
/// developer's laptop, and it is testable without either.</para>
/// </summary>
public sealed class DevDataSeederGuardTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("UAT")]
    public async Task Refuses_to_run_outside_Development(string environmentName)
    {
        // The null AppDbContext and UserManager are the assertion's teeth: if the guard did not throw
        // first, this would fail with a NullReferenceException instead, and the test would still be red -
        // but for the wrong reason, so the message is checked too.
        var act = async () => await DevDataSeeder.SeedAsync(null!, null!, EmptyConfiguration, new Env(environmentName));

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("*must never run outside*");
        // The environment is named, because "it refused" without saying where leaves an operator guessing
        // which host they are looking at.
        thrown.WithMessage($"*{environmentName}*");
    }

    [Fact]
    public async Task Gets_past_the_guard_in_Development()
    {
        // The control. Without it, a guard that threw unconditionally would pass every test above, and the
        // seeder would be dead in the one environment it exists for - which is a plausible mistake, since
        // nothing else in the suite runs it.
        var act = async () => await DevDataSeeder.SeedAsync(null!, null!, EmptyConfiguration, new Env("Development"));

        // It fails on the null database, NOT on the guard: past the refusal, and no further.
        await act.Should().ThrowAsync<NullReferenceException>();
    }
}
