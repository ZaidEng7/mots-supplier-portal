// The environment guard on the development data seeder.
//
// That seeder creates eight accounts whose password is a constant published in the runbook. For a while the only
// thing standing between that and production was an environment check around the call site, so a second call
// added anywhere, or that block being widened, would have seeded known credentials into whatever environment was
// running.
//
// The refusal now lives in the seeder, where the dangerous knowledge is.
//
//
// WHY THIS TEST AND NOT A FULLER ONE
//
// Everything past the guard needs a database and a user manager, and the integration fixture deliberately does
// not run this seeder: it runs as development, so before a setting gated it, its suppliers turned up in the
// middle of another suite's assertions.
//
// The guard is the part whose failure has consequences beyond a developer's laptop, and it is testable without
// either dependency.
//
//
// THE NULL DEPENDENCIES ARE THE ASSERTION'S TEETH
//
// If the guard did not throw first, the call would fail on a null reference instead. The test would still be red,
// but for the wrong reason, so the message is checked too.
//
// The environment is named in that message, because "it refused" without saying where leaves an operator
// guessing which host they are looking at.
//
//
// THE CONTROL
//
// Without it, a guard that threw unconditionally would pass every assertion above, and the seeder would be dead
// in the one environment it exists for. That is a plausible mistake, because nothing else in the suite runs it.
//
// The control asserts the call fails on the null database and NOT on the guard: past the refusal, and no
// further.

namespace MotsSupplierPortal.Tests.Unit.Identity;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using MotsSupplierPortal.Infrastructure.Identity;

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
        var act = async () => await DevDataSeeder.SeedAsync(null!, null!, EmptyConfiguration, new Env(environmentName));

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("*must never run outside*");
        thrown.WithMessage($"*{environmentName}*");
    }

    [Fact]
    public async Task Gets_past_the_guard_in_Development()
    {
        var act = async () => await DevDataSeeder.SeedAsync(null!, null!, EmptyConfiguration, new Env("Development"));

        await act.Should().ThrowAsync<NullReferenceException>();
    }
}
